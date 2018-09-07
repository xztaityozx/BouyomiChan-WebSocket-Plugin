//�v���O�C���̃t�@�C�����́A�uPlugin_*.dll�v�Ƃ����`���ɂ��ĉ������B
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FNF.BouyomiChanApp;
using FNF.Utility;
using FNF.XmlSerializerSetting;
using System.Linq;

namespace Plugin_WebSocket {
    public class Plugin_WebSocket : IPlugin {
        private Accept wsAccept;


        public string Name => "WebSocket�T�[�o�[";
        public string Version => "2018/09/07��";
        public string Caption => "WebSocket����̓ǂݏグ���N�G�X�g���󂯕t���܂��Bchocoa/BouyomiChan-WebSocket-Plugin��Fork�ł�";

        public ISettingFormData SettingFormData //�v���O�C���̐ݒ��ʏ��i�ݒ��ʂ��K�v�Ȃ����null��Ԃ��Ă��������j
            => null;

        //�v���O�C���J�n������
        public void Begin() {
            wsAccept = new Accept(50002);
            wsAccept.Start();
        }

        //�v���O�C���I��������
        public void End() {
            wsAccept.Stop();
        }


        // ��t�N���X
        private class Accept {
            private bool active = true;
            private readonly int mPort;
            private Socket server;
            private Thread thread;

            // �R���X�g���N�^
            public Accept(int port) {
                mPort = port;
            }

            public void Start() {
                thread = new Thread(Run);
                Pub.AddTalkTask("�\�P�b�g��t���J�n���܂����B", -1, -1, VoiceType.Default);
                thread.Start();
            }

            public void Stop() {
                active = false;
                server.Close();
                thread.Abort();
                Pub.AddTalkTask("�\�P�b�g��t���I�����܂����B", -1, -1, VoiceType.Default);
            }

            private void Run() {
                // �T�[�o�[�\�P�b�g������
                server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var ip = IPAddress.Parse("0.0.0.0");
                var ipEndPoint = new IPEndPoint(ip, mPort);

                server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                server.Bind(ipEndPoint);
                server.Listen(5);

                // �v���҂��i�������[�v�j
                while (active) {
                    var client = server.Accept();
                    var response = new Response(client);
                    response.Start();
                }
            }
        }


        // �����N���X
        private class Response {
            private readonly Socket mClient;
            private readonly Status mStatus;

            // �R���X�g���N�^
            public Response(Socket client) {
                mClient = client;
                mStatus = Status.Checking;
            }

            // �����J�n
            public void Start() {
                var thread = new Thread(Run);
                thread.Start();
            }

            // �������s
            private void Run() {
                try {
                    // �v����M
                    var bsize = mClient.ReceiveBufferSize;
                    var buffer = new byte[bsize];
                    var recvLen = mClient.Receive(buffer);

                    if (recvLen <= 0)
                        return;

                    var header = Encoding.ASCII.GetString(buffer, 0, recvLen);
                    Debug.WriteLine("�y" + System.DateTime.Now + "�z\n" + header);

                    // �v��URL�m�F �� �������e����
                    var pos = header.IndexOf("GET / HTTP/", StringComparison.Ordinal);

                    if (mStatus == Status.Checking && 0 == pos) DoWebSocketMain(header);
                }
                catch (SocketException e) {
                    Debug.Write(e.Message);
                }
                finally {
                    mClient.Close();
                }
            }

            // WebSocket���C��
            private void DoWebSocketMain(string header) {
                const string key = "Sec-WebSocket-Key: ";
                var pos = header.IndexOf(key, StringComparison.Ordinal);
                if (pos < 0) return;

                // "Sec-WebSocket-Accept"�ɐݒ肷�镶����𐶐�
                var value = header.Substring(pos + key.Length, header.IndexOf("\r\n", pos, StringComparison.Ordinal) - (pos + key.Length));
                var byteValue = Encoding.UTF8.GetBytes(value + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
                SHA1 crypto = new SHA1CryptoServiceProvider();
                var hash = crypto.ComputeHash(byteValue);
                var resValue = Convert.ToBase64String(hash);

                // �������e���M
                var buffer = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 101 OK\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    "Sec-WebSocket-Accept: " + resValue + "\r\n" +
                    "\r\n");

                mClient.Send(buffer);

                // �N���C�A���g����e�L�X�g����M
                var bsize = mClient.ReceiveBufferSize;
                var request = new byte[bsize];
                mClient.Receive(request);

                // �}�X�N����
                long payloadLen = request[1] & 0x7F;
                var masked = (request[1] & 0x80) == 0x80;
                var hp = 2;
                switch (payloadLen) {
                    case 126:
                        payloadLen = request[2] * 0x100 + request[3];
                        hp += 2;
                        break;
                    case 127:
                        payloadLen = request[2] * 0x100000000000000 + request[3] * 0x1000000000000 +
                                     request[4] * 0x10000000000 + request[5] * 0x100000000 + request[6] * 0x1000000 +
                                     request[7] * 0x10000 + request[8] * 0x100 + request[9];
                        hp += 8;
                        break;
                }

                if (masked) {
                    for (var i = 0; i < payloadLen; i++)
                        request[hp + 4 + i] ^= request[hp + i % 4];
                    //Console.WriteLine(buffer[6 + i]);
                    hp += 4;
                }

                // �󂯎�������N�G�X�g�̉��
                var fromClient = Encoding.UTF8.GetString(request, hp, (int) payloadLen);
                Debug.WriteLine(fromClient);

                string[] delim = {"<bouyomi>"};
                var param = fromClient.Split(delim, 6, StringSplitOptions.None);
                var vt = (VoiceType) int.Parse(param[4]);
                var text = param[5];
                var values = param.Take(5).Select(int.Parse).ToArray();

                switch (values[0]) {
                    case 0x0001: Pub.AddTalkTask(text, values[1], values[2], values[3], vt);break;
                    case 0x0010: Pub.Pause = true;break;
                    case 0x0020: Pub.Pause = false;break;
                    case 0x0030: Pub.SkipTalkTask();break;
                }
            }

            private enum Status {
                Checking // ERROR
            }
        }
    }
}