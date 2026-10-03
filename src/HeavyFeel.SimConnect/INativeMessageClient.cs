namespace HeavyFeel.SimConnect;

public interface INativeMessageClient
{
    int MessageId { get; }
    void Receive();
}
