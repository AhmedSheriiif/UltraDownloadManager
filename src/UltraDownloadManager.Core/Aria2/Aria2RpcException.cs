namespace UltraDownloadManager.Core.Aria2;

public sealed class Aria2RpcException : Exception
{
    public int Code { get; }

    public Aria2RpcException(int code, string message) : base(message)
    {
        Code = code;
    }
}
