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
    public string topic = "grpu0Lh09cJE1M";
    public string create = "nch";

    public InputField msgInput;
    public InputField msgContent;

    private Queue<ServerMsg> msgQueue = new Queue<ServerMsg>();

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        while(msgQueue.Count > 0)
        {
            var msg = msgQueue.Dequeue();
            if (msg.Data != null)
            {
                var data = msg.Data.Content.ToStringUtf8();
                var content = JsonConvert.DeserializeObject<ContentMsg>(data);
                DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                dateTime = dateTime.AddMilliseconds(msg.Data.Timestamp).ToLocalTime();
                var bytes = Convert.FromBase64String(msg.Data.FromUserId + "==");
                var userId = BitConverter.ToInt64(bytes, 0);
                this.msgContent.text += string.Format("[{0} : {1}] {2}\n", dateTime, userId, content.txt);
            }
            if (msg.Ctrl != null)
            {
                //if (msg.Ctrl.Topic != null)
                //{
                //    Debug.LogFormat("Sub Topic {0}", msg.Ctrl.Topic);
                //} else if (msg.Ctrl.Params != null)
                //{
                //    Debug.LogFormat("Login ok {0}", msg.Ctrl.Params.ToString());
                //}
            }
            
        }
    }

    public void Connect()
    {
        //var options = new List<ChannelOption>
        //{
        //    new ChannelOption("grpc.keepalive_time_ms", 1000),
        //    new ChannelOption("grpc.keepalive_timeout_ms", 3000)
        //};

        channel = new Channel(host, ChannelCredentials.Insecure);

        var stub = new Node.NodeClient(channel);

        client = stub.MessageLoop(cancellationToken: cancellationTokenSource.Token);
        ReceiveMessageLoop();
        SendMessageLoop();
        Hello();
        Debug.Log("Completed");
    }

    public void Disconnect()
    {
        cancellationTokenSource.Cancel();
        channel.ShutdownAsync().Wait();
        Debug.Log("Disconnected");
    }

    public void Login()
    {
        var tid = GetNextTid();

        ClientMsg msg = new ClientMsg() { Login = new ClientLogin() { Id = tid, Scheme = schema, Secret = ByteString.CopyFromUtf8(secret) } };
        ClientPost(msg);
        Debug.Log("Login");
    }

    public void CreateTopic()
    {
        var tid = GetNextTid();
        var msg = new ClientMsg() { Sub = new ClientSub() { Id = tid, Topic = create + topic } };
        ClientPost(msg);
    }

    public void SubTopic()
    {
        var tid = GetNextTid();
        var msg = new ClientMsg() { Sub = new ClientSub() { Id = tid, Topic = topic } };
        ClientPost(msg);
    }

    public void FetchMsg()
    {
        var tid = GetNextTid();
        var msg = new ClientMsg()
        {
            Get = new ClientGet()
            {
                Id = tid,
                Topic = topic,
                Query = new GetQuery()
                {
                    What = "data",
                    Data = new GetOpts()
                    {
                        Limit = 25
                    }
                }
            }
        };
        Debug.Log("FetchMsg");
        ClientPost(msg);
    }

    public void SendMsg()
    {
        if (msgInput.text.Length == 0) return;
        var tid = GetNextTid();
        var message = JsonConvert.SerializeObject(new ContentMsg() { txt = msgInput.text });
        Debug.LogFormat("SendMsg: {0}", message);
        var content = ByteString.CopyFromUtf8(message);
        var pub = new ClientPub()
        {
            Id = tid,
            Topic = topic,
            NoEcho = false,
            Content = content,
        };
        var msg = new ClientMsg() { Pub = pub, AuthLevel = AuthLevel.Auth };
        ClientPost(msg);

        msgInput.text = "";
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
                        //sendMsgQueue.Enqueue(msg);
                        //Thread.Sleep(1000);
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
                var msg = client.ResponseStream.Current;
                msgQueue.Enqueue(msg);
                Debug.Log(msg.ToString());
            }
        }, cancellationTokenSource.Token);
        receiveBackendTask.Start();
    }

    private void OnDisable()
    {
        Disconnect();
    }
}
