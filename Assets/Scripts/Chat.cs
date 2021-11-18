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
        while (client != null && client.ResponseStream.Current > 0)
        {
            var msg = sendMsgQueue.Dequeue();
            try
            {
                Debug.Log("send msg start");
                await client.RequestStream.WriteAsync(msg);
                Debug.Log("send msg done");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
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
        SendMessageLoop();
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
        AddFuture(tid, new Future(tid, Future.FutureTypes.Login, new Action<string, MapField<string, ByteString>>((fname, paramaters) =>
        {
            OnLogin(fname, paramaters);
        }), cookie));

        ClientMsg msg = new ClientMsg() { Login = new ClientLogin() { Id = tid, Scheme = schema, Secret = ByteString.CopyFromUtf8(secret) } };
        ClientPost(msg);
        Debug.Log("Login");
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

    public async Task ClientMessageLoop()
    {
        while (!cancellationTokenSource.IsCancellationRequested)
        {
            if (!await client.ResponseStream.MoveNext())
            {
                break;
            }
            var response = client.ResponseStream.Current;
            if (response.Ctrl != null)
            {
                Debug.Log($"ID={response.Ctrl.Id}  Code={response.Ctrl.Code}  Text={response.Ctrl.Text}  Params={response.Ctrl.Params}");
                ExecFuture(response.Ctrl.Id, response.Ctrl.Code, response.Ctrl.Text, response.Ctrl.Topic, response.Ctrl.Params);
            }
            else if (response.Data != null)
            {
                OnServerDataEvent(new ServerDataEventArgs(response.Data.Clone()));
                if (response.Data.FromUserId != BotUID)
                {
                    ClientPost(NoteRead(response.Data.Topic, response.Data.SeqId));
                    Thread.Sleep(50);
                    if (BotResponse != null)
                    {
                        var reply = await BotResponse.ThinkAndReply(response.Data.Clone());
                        //if the response is null, means no need to reply
                        if (reply != null)
                        {
                            ClientPost(Publish(response.Data.Topic, reply));
                        }

                    }
                    else
                    {
                        ClientPost(Publish(response.Data.Topic, "I don't know how to talk with you, maybe my father didn't put my brain in my head..."));
                    }

                }
            }
            else if (response.Pres != null)
            {
                if (response.Pres.Topic == "me")
                {
                    if ((response.Pres.What == ServerPres.Types.What.On || response.Pres.What == ServerPres.Types.What.Msg) && !subscriptions.ContainsKey(response.Pres.Src))
                    {
                        ClientPost(Subscribe(response.Pres.Src));

                    }
                    else if (response.Pres.What == ServerPres.Types.What.Off && subscriptions.ContainsKey(response.Pres.Src))
                    {
                        ClientPost(Leave(response.Pres.Src));
                    }
                }

                OnServerPresEvent(new ServerPresEventArgs(response.Pres.Clone()));
            }
            else if (response.Meta != null)
            {
                OnGetMeta(response.Meta);
                OnServerMetaEvent(new ServerMetaEventArgs(response.Meta.Clone()));
            }
        }
    }
}
