using System;
using Google.Protobuf;
using Google.Protobuf.Collections;

public class Future
{
    public enum FutureTypes
    {
        /// <summary>
        /// defatul, unknown callback operation
        /// </summary>
        Unknown,
        /// <summary>
        /// Hi rpc call
        /// </summary>
        Hi,
        /// <summary>
        /// login rpc call
        /// </summary>
        Login,
        /// <summary>
        /// sub rpc call
        /// </summary>
        Sub,
        /// <summary>
        /// get rpc call
        /// </summary>
        Get,
        /// <summary>
        /// pub rpc call
        /// </summary>
        Pub,
        /// <summary>
        /// note rpc call
        /// </summary>
        Note,
        /// <summary>
        /// leave rpc call
        /// </summary>
        Leave,
    }

    /// <summary>
    /// Each rpc call message id
    /// </summary>
    public string Tid { get; private set; }
    /// <summary>
    /// Argument needs by action.
    /// </summary>
    public string Arg { get; private set; }
    /// <summary>
    /// Future action type
    /// </summary>
    public FutureTypes Type { get; private set; }
    /// <summary>
    /// callback function
    /// </summary>
    public Action<string, MapField<string, ByteString>> Action { get; private set; }
    /// <summary>
    /// construction
    /// </summary>
    /// <param name="tid"> Each rpc call message id</param>
    /// <param name="action">Argument needs by action.</param>
    /// <param name="arg">callback function</param>
    public Future(string tid, FutureTypes type, Action<string, MapField<string, ByteString>> action, string arg = "")
    {
        Tid = tid;
        Type = type;
        Action = action;
        Arg = arg;
    }
}
