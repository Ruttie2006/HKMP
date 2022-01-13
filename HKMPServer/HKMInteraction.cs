using Hkmp.Game.Server;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
using System.Linq;
using Hkmp;

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
                Listener.BeginGetContext(OnReceived, Listener);
            }
            catch (Exception ex)
            {
                Logger.Get().Error(this, ex.ToString());
            }
        }
        public async void OnReceived(IAsyncResult res) =>
            await HandleReceived(Listener.EndGetContext(res));
        
        public async Task HandleReceived(HttpListenerContext context)
        {
            try
            {
                var url = context.Request.RawUrl;
                if (url == "/hkmp/list")
                {
                    context.Response.KeepAlive = false;
                    if (context.Response.ContentEncoding == null)
                        context.Response.ContentEncoding = Encoding.Unicode;
                    var names = Manager.GetPlayerNames();
                    var serializer = new DataContractJsonSerializer(typeof(string[]));
                    serializer.WriteObject(context.Response.OutputStream, names);
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
                Logger.Get().Error(this, ex.ToString());
            }
        }
    }
}
