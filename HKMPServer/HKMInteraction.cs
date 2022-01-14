using Hkmp.Game.Server;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
using System.Linq;
using Hkmp;
using System.Threading;
using Hkmp.Game;
using System.Collections.Generic;

namespace HKMPServer.HKM
{
    internal class HKMInteraction
    {
        private HttpListener Listener { get; set; }
        private ServerManager Manager { get; set; }
        public HKMInteraction(ref ServerManager _ServerManager)
        {
            try
            {
                Manager = _ServerManager;
                Listener = new HttpListener();
                Listener.Prefixes.Add(@"http://192.168.1.65:5051/");
                Listener.Start();

                new Thread(async () =>
                {
                    try
                    {
                        while (Listener.IsListening)
                            Listener.BeginGetContext(OnReceived, Listener);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(this, ex.ToString());
                    }
                    //Wait a little to prevent spam
                    await Task.Delay(100);
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal,
                    Name = "HKM Interaction Thread"
                }.Start();
            }
            catch (Exception ex)
            {
                Logger.Log.Error(this, ex.ToString());
            }
        }
        public async void OnReceived(IAsyncResult res) =>
            await HandleReceived(Listener.EndGetContext(res));

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task HandleReceived(HttpListenerContext context)
        {
            try
            {
                Console.WriteLine("Received request");
                var url = context.Request.RawUrl;
                if (url.EndsWith("/hkmp/list"))
                {
                    context.Response.KeepAlive = false;
                    if (context.Response.ContentEncoding == null)
                        context.Response.ContentEncoding = Encoding.Unicode;
                    var names = Manager.GetPlayerNames();
                    var serializer = new DataContractJsonSerializer(typeof(string[]));
                    serializer.WriteObject(context.Response.OutputStream, names);
                    context.Response.Close();
                }
                else if (url.StartsWith("/hkmp/list/"))
                {
                    var name = url.Replace("/hkmp/list/", "");
                    var users = Manager.GetPlayerData().Where((s) => s.Username.ToLower() == name.ToLower()).ToArray();
                    var SendableData = new SendableServerUserData[users.Count()];
                    int added = 0;
                    foreach (var user in users)
                        SendableData[added++] = (SendableServerUserData)user;
                    var ToSend = SendableServerUserData.SerializeArray(SendableData);
                    if (context.Response.ContentEncoding == null)
                        context.Response.ContentEncoding = Encoding.Unicode;
                    var buffer = context.Response.ContentEncoding.GetBytes(ToSend);
                    context.Response.OutputStream.Write(buffer, 0, buffer.Count() - 1);
                    context.Response.Close();
                }
                else
                {
                    if (url.Contains("hkmp"))
                    {
                        context.Response.KeepAlive = false;
                        if (context.Response.ContentEncoding == null)
                            context.Response.ContentEncoding = Encoding.Unicode;
                        byte[] bytes = context.Response.ContentEncoding.GetBytes("Command not found.");
                        context.Response.OutputStream.Write(bytes, 0, bytes.Count() - 1);
                        context.Response.Close();
                    }
                    else
                        context.Response.Abort();
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Error(this, ex.ToString());
            }
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        private class SendableServerUserData
        {
            public string Username { get; set; }
            public string Scene { get; set; }
            public Team PlayerTeam { get; set; }
            public byte SkinID { get; set; }
            public static explicit operator SendableServerUserData(ServerPlayerData info)
            {
                return new SendableServerUserData()
                {
                    Username = info.Username,
                    Scene = info.CurrentScene,
                    PlayerTeam = info.Team,
                    SkinID = info.SkinId,
                };
            }
            public static string SerializeArray(SendableServerUserData[] data)
            {
                string res = "";
                foreach (var item in data)
                    res += $"{item.Username},{item.Scene},{item.PlayerTeam},{item.SkinID}|";
                return res;
            }
            public static SendableServerUserData[] DeSerializeArray(string arr)
            {
                List<SendableServerUserData> datas = new List<SendableServerUserData>();
                int amountAdded = 0;
                foreach (var item in arr.Split('|')) {
                    string name = "";
                    string scene = "";
                    string team = "";
                    string SkinId = "";
                    foreach (var field in item.Split(',')) {
                        if (amountAdded == 0)
                            name = field;
                        else if (amountAdded == 1)
                            scene = field;
                        else if (amountAdded == 2)
                            team = field;
                        else if (amountAdded == 3)
                            SkinId = field;
                        amountAdded++;
                    }
                    datas.Add(new SendableServerUserData() {Username = name, PlayerTeam = (Team)Enum.Parse(typeof(Team), team), Scene = scene, SkinID = byte.Parse(SkinId) });
                    amountAdded = 0;
                }
                return datas.ToArray();
            }
        }

    }
}
