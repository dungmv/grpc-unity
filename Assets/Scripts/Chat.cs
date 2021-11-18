using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Core;
using Newtonsoft.Json;
using Pbx;
using UnityEngine;

public class Chat : MonoBehaviour
{
    public string ServerHost = "localhost";

    private Queue<ClientMsg> sendMsgQueue = new Queue<ClientMsg>();
    private Dictionary<string, Future> onCompletion = new Dictionary<string, Future>();
    private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    private AsyncDuplexStreamingCall<ClientMsg, ServerMsg> client;
    private Channel channel;
    private long NextTid { get; set; }
    private string AppName => "ChatBot";
    /// <summary>
    /// Chatbot version
    /// </summary>
    private string AppVersion => "0.16.0";
    /// <summary>
    /// Chatbot library version
    /// </summary>
    private string LibVersion => "0.16.0";
    /// <summary>
    /// Chatbot current platfrom information
    /// </summary>
    private string Platform => $"({RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture})";
    private string cookie = ".tn-cookie";
    private string schema = "basic";
    private string secret = "";

    // Start is called before the first frame update
    void Start()
    {
        NextTid = UnityEngine.Random.Range(1, 1000);
    }

    // Update is called once per frame
    async void Update()
    {
        while (client != null && sendMsgQueue.Count > 0)
        {
            var msg = sendMsgQueue.Dequeue();
            try
            {
                await client.RequestStream.WriteAsync(msg);
            }
            catch (Exception e)
            {
                Debug.LogFormat("Send Message Error {}, Failed message will be put back to queue...", e.Message);
                sendMsgQueue.Enqueue(msg);
                Thread.Sleep(1000);
            }

        }
    }

    public void Connect()
    {
        var options = new List<ChannelOption>
        {
            new ChannelOption("grpc.keepalive_time_ms", 2000),
            new ChannelOption("grpc.keepalive_timeout_ms", 2000)
        };

        channel = new Channel(ServerHost, ChannelCredentials.Insecure, options);

        var stub = new Node.NodeClient(channel);

        client = stub.MessageLoop(cancellationToken: cancellationTokenSource.Token);
    }

    public void Disconnect()
    {
        cancellationTokenSource.Cancel();
        channel.ShutdownAsync().Wait();
    }

    public void OnLogin(string cookieFile, MapField<string, ByteString> paramaters)
    {
        if (paramaters == null || string.IsNullOrEmpty(cookieFile))
        {
            return;
        }
        //if (paramaters.ContainsKey("user"))
        //{
        //    BotUID = paramaters["user"].ToString(Encoding.ASCII);
        //}
        Dictionary<string, string> cookieDics = new Dictionary<string, string>();
        cookieDics["schema"] = "token";
        if (paramaters.ContainsKey("token"))
        {
            cookieDics["secret"] = JsonConvert.DeserializeObject<string>(paramaters["token"].ToString(Encoding.UTF8));
            cookieDics["expires"] = JsonConvert.DeserializeObject<string>(paramaters["expires"].ToString(Encoding.UTF8));
        }
        else
        {
            cookieDics["schema"] = "basic";
            cookieDics["secret"] = JsonConvert.DeserializeObject<string>(paramaters["token"].ToString(Encoding.UTF8));
        }
        //save token for upload operation
        var token = cookieDics["secret"];
        try
        {
            using (FileStream stream = new FileStream(cookieFile, FileMode.Create, FileAccess.Write))
            using (StreamWriter w = new StreamWriter(stream))
            {
                w.Write(JsonConvert.SerializeObject(cookieDics));
            }

        }
        catch (Exception e)
        {
            Debug.Log($"On Login Failed to save authentication cookie:{e}");
        }

    }
    public void Login()
    {
        var tid = GetNextTid();
        AddFuture(tid, new Future(tid, Future.FutureTypes.Login, new Action<string, MapField<string, ByteString>>((fname, paramaters) =>
        {
            OnLogin(fname, paramaters);
        }), cookie));

        ClientMsg msg = new ClientMsg() { Login = new ClientLogin() { Id = tid, Scheme = schema, Secret = ByteString.CopyFromUtf8(secret) } };
        ClientPost(msg);
    }

    public void Hi()
    {
        var tid = GetNextTid();
        AddFuture(tid, new Future(tid, Future.FutureTypes.Hi, new Action<string, MapField<string, ByteString>>((unused, paramaters) =>
        {
            ServerVersion(paramaters);
        })));

        ClientMsg msg = new ClientMsg() { Hi = new ClientHi() { Id = tid, UserAgent = $"{AppName}/{AppVersion} {Platform}; gRPC-csharp/{AppVersion}", Ver = LibVersion, Lang = "EN" } };

        ClientPost(msg);
    }

    public void AddFuture(string tid, Future bundle)
    {
        onCompletion.Add(tid, bundle);
    }

    public void ServerVersion(MapField<string, ByteString> paramaters)
    {
        if (paramaters == null)
        {
            return;
        }
        Debug.Log($"Server Version Server:{paramaters["build"].ToString(Encoding.ASCII)},{paramaters["ver"].ToString(Encoding.ASCII)}");
    }

    public string GetNextTid()
    {
        NextTid += 1;
        return NextTid.ToString();
    }

    public void ClientPost(ClientMsg msg)
    {
        if (client != null)
        {
            sendMsgQueue.Enqueue(msg);
        }
    }
}
