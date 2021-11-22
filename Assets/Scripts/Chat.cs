using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Grpc.Core;
using Newtonsoft.Json;
using Pbx;
using UnityEngine;
using UnityEngine.UI;

public class Chat : MonoBehaviour
{
    private Queue<ClientMsg> sendMsgQueue = new Queue<ClientMsg>();
    private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    private AsyncDuplexStreamingCall<ClientMsg, ServerMsg> client;
    private Channel channel;
    private string AppName => "zombiewar";
    private string AppVersion => "0.16.0";
    private string LibVersion => "0.16.0";
    private string Platform => $"({RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture})";
    public string schema = "rest";
    public string secret = "alice:alice123";
    public string host = "127.0.0.1:16060";
    private string topic = "grp_61hMsTfclY";

    public InputField msgInput;
    public InputField msgContent;

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
    }

    public void Connect()
    {
        var options = new List<ChannelOption>
        {
            new ChannelOption("grpc.keepalive_time_ms", 2000),
            new ChannelOption("grpc.keepalive_timeout_ms", 2000)
        };

        channel = new Channel(host, ChannelCredentials.Insecure, options);

        var stub = new Node.NodeClient(channel);

        client = stub.MessageLoop(cancellationToken: cancellationTokenSource.Token);
        ReceiveMessageLoop();
        SendMessageLoop();
        Hello();
        Login();
        SubGuild();
        Debug.Log("Completed");
    }

    public void Disconnect()
    {
        cancellationTokenSource.Cancel();
        channel.ShutdownAsync().Wait();
        Debug.Log("Disconnected");
    }

    private void Login()
    {
        var tid = GetNextTid();

        ClientMsg msg = new ClientMsg() { Login = new ClientLogin() { Id = tid, Scheme = schema, Secret = ByteString.CopyFromUtf8(secret) } };
        ClientPost(msg);
        Debug.Log("Login");
    }

    private void CreateTopic()
    {
        var tid = GetNextTid();
        var msg = new ClientMsg() { Sub = new ClientSub() { Id = tid, Topic = "new"} };
        ClientPost(msg);
    }

    private void SubGuild()
    {
        var tid = GetNextTid();
        var msg = new ClientMsg() { Sub = new ClientSub() { Id = tid, Topic = topic } };
        ClientPost(msg);
    }

    public void SendMsg()
    {
        if (msgInput.text.Length == 0) return;
        var tid = GetNextTid();
        var message = JsonConvert.SerializeObject(new { txt = msgInput.text });
        Debug.LogFormat("SendMsg: {0}", message);
        var content = ByteString.CopyFromUtf8(message);
        var pub = new ClientPub()
        {
            Id = tid,
            Topic = topic,
            NoEcho = false,
            Content = content
        };
        var msg = new ClientMsg() { Pub = pub };
        ClientPost(msg);

        msgInput.text = "";
    }

    private void OnLogin(string cookieFile, MapField<string, ByteString> paramaters)
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
            cookieDics["schema"] = "token";
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
            Debug.LogException(e);
        }

    }

    private void Hello()
    {
        var tid = GetNextTid();

        ClientMsg msg = new ClientMsg() { Hi = new ClientHi() { Id = tid, UserAgent = $"{AppName}/{AppVersion} {Platform}; gRPC-csharp/{AppVersion}", Ver = LibVersion, Lang = "EN" } };

        ClientPost(msg);
        Debug.Log("Hi");
    }

    private string GetNextTid()
    {
        return Guid.NewGuid().ToString();
    }

    private void ClientPost(ClientMsg msg)
    {
        sendMsgQueue.Enqueue(msg);
    }

    private void SendMessageLoop()
    {
        Task sendBackendTask = new Task(async () =>
        {
            Debug.Log("Start Message Queue, Message send queue started...");
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (sendMsgQueue.Count > 0)
                {
                    var msg = sendMsgQueue.Dequeue();
                    try
                    {
                        await client.RequestStream.WriteAsync(msg);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(e);
                        sendMsgQueue.Enqueue(msg);
                        Thread.Sleep(1000);
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            Debug.Log("User Cancel, Detect cancel message,stop sending message...");
        }, cancellationTokenSource.Token);
        sendBackendTask.Start();

    }

    private void ReceiveMessageLoop()
    {
        Task receiveBackendTask = new Task(async () =>
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (!await client.ResponseStream.MoveNext())
                {
                    break;
                }
                var response = client.ResponseStream.Current;

                Debug.Log(response.ToString());
            }
        }, cancellationTokenSource.Token);
        receiveBackendTask.Start();
    }

    private void OnDisable()
    {
        Disconnect();
    }
}
