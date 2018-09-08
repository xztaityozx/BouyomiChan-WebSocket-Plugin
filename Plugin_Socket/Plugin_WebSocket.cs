//プラグインのファイル名は、「Plugin_*.dll」という形式にして下さい。
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


        public string Name => "WebSocketサーバー";
        public string Version => "2018/09/08版";
        public string Caption => "WebSocketからの読み上げリクエストを受け付けます。chocoa/BouyomiChan-WebSocket-PluginのForkです";

        public ISettingFormData SettingFormData //プラグインの設定画面情報（設定画面が必要なければnullを返してください）
            => null;

        //プラグイン開始時処理
        public void Begin() {
            wsAccept = new Accept(50002);
            wsAccept.Start();
        }

        //プラグイン終了時処理
        public void End() {
            wsAccept.Stop();
        }


        // 受付クラス
        private class Accept {
            private bool active = true;
            private readonly int mPort;
            private Socket server;
            private Thread thread;

            // コンストラクタ
            public Accept(int port) {
                mPort = port;
            }

            public void Start() {
                thread = new Thread(Run);
                Pub.AddTalkTask("ソケット受付を開始しました。", -1, -1, VoiceType.Default);
                thread.Start();
            }

            public void Stop() {
                active = false;
                server.Close();
                thread.Abort();
                Pub.AddTalkTask("ソケット受付を終了しました。", -1, -1, VoiceType.Default);
            }

            private void Run() {
                // サーバーソケット初期化
                server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var ip = IPAddress.Parse("0.0.0.0");
                var ipEndPoint = new IPEndPoint(ip, mPort);

                server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                server.Bind(ipEndPoint);
                server.Listen(5);

                // 要求待ち（無限ループ）
                while (active) {
                    var client = server.Accept();
                    var response = new Response(client);
                    response.Start();
                }
            }
        }


        // 応答クラス
        private class Response {
            private readonly Socket mClient;
            private readonly Status mStatus;

            // コンストラクタ
            public Response(Socket client) {
                mClient = client;
                mStatus = Status.Checking;
            }

            // 応答開始
            public void Start() {
                var thread = new Thread(Run);
                thread.Start();
            }

            // 応答実行
            private void Run() {
                try {
                    // 要求受信
                    var bsize = mClient.ReceiveBufferSize;
                    var buffer = new byte[bsize];
                    var recvLen = mClient.Receive(buffer);

                    if (recvLen <= 0)
                        return;

                    var header = Encoding.ASCII.GetString(buffer, 0, recvLen);
                    Debug.WriteLine("【" + System.DateTime.Now + "】\n" + header);

                    // 要求URL確認 ＆ 応答内容生成
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

            // WebSocketメイン
            private void DoWebSocketMain(string header) {
                const string key = "Sec-WebSocket-Key: ";
                var pos = header.IndexOf(key, StringComparison.Ordinal);
                if (pos < 0) return;

                // "Sec-WebSocket-Accept"に設定する文字列を生成
                var value = header.Substring(pos + key.Length, header.IndexOf("\r\n", pos, StringComparison.Ordinal) - (pos + key.Length));
                var byteValue = Encoding.UTF8.GetBytes(value + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
                SHA1 crypto = new SHA1CryptoServiceProvider();
                var hash = crypto.ComputeHash(byteValue);
                var resValue = Convert.ToBase64String(hash);

                // 応答内容送信
                var buffer = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 101 OK\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    "Sec-WebSocket-Accept: " + resValue + "\r\n" +
                    "\r\n");

                mClient.Send(buffer);

                // クライアントからテキストを受信
                var bsize = mClient.ReceiveBufferSize;
                var request = new byte[bsize];
                mClient.Receive(request);

                // マスク解除
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

                // 受け取ったリクエストの解析
                var fromClient = Encoding.UTF8.GetString(request, hp, (int) payloadLen);
                Debug.WriteLine(fromClient);

                string[] delim = {"<bouyomi>"};
                var param = fromClient.Split(delim, 6, StringSplitOptions.RemoveEmptyEntries);
                try {
                    var vt = (VoiceType) int.Parse(param[4]);
                    var text = param[5];
                    var values = param.Take(5).Select(int.Parse).ToArray();

                    switch (values[0]) {
                        case 0x0001:
                            Pub.AddTalkTask(text, values[1], values[2], values[3], vt);
                            break;
                        case 0x0010:
                            Pub.Pause = true;
                            break;
                        case 0x0020:
                            Pub.Pause = false;
                            break;
                        case 0x0030:
                            Pub.SkipTalkTask();
                            break;
                    }
                }
                catch (Exception e) {
                    Pub.AddTalkTask("WebSocketサーバー内でエラーが起きました・・・");
                }
            }

            private enum Status {
                Checking // ERROR
            }
        }
    }
}