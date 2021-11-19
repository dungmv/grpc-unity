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

public class Chat : MonoBehaviour
{
    public string ServerHost = "localhost";

    private Queue<ClientMsg> sendMsgQueue = new Queue<ClientMsg>();
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

        channel = new Channel(ServerHost, ChannelCredentials.Insecure, options);

        var stub = new Node.NodeClient(channel);

        client = stub.MessageLoop(cancellationToken: cancellationTokenSource.Token);
        SendMessageLoop();
        ReceiveMessageLoop();
        Debug.Log("Connected");
    }

    public void Disconnect()
    {
        cancellationTokenSource.Cancel();
        channel.ShutdownAsync().Wait();
        Debug.Log("Disconnected");
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
            Debug.LogFormat("On Login Failed to save authentication cookie: {}", e);
        }

    }
    public void Login()
    {
        var tid = GetNextTid();

        ClientMsg msg = new ClientMsg() { Login = new ClientLogin() { Id = tid, Scheme = schema, Secret = ByteString.CopyFromUtf8(secret) } };
        ClientPost(msg);
        Debug.Log("Login");
    }

    public void Hi()
    {
        var tid = GetNextTid();

        ClientMsg msg = new ClientMsg() { Hi = new ClientHi() { Id = tid, UserAgent = $"{AppName}/{AppVersion} {Platform}; gRPC-csharp/{AppVersion}", Ver = LibVersion, Lang = "EN" } };

        ClientPost(msg);
        Debug.Log("Hi");
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
    /// <summary>
    /// sending message queue loop
    /// </summary>
    public void SendMessageLoop()
    {
        Task sendBackendTask = new Task(async () =>
        {
            Debug.Log("Start Message Queue Message send queue started...");
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
                        Debug.LogException(e);
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            Debug.Log("User Cancel Detect cancel message,stop sending message...");
        }, cancellationTokenSource.Token);
        sendBackendTask.Start();

    }

    public void ReceiveMessageLoop()
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

                Debug.Log( response.ToString() );
            }
        }, cancellationTokenSource.Token);
        receiveBackendTask.Start();
    }
}
