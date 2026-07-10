namespace RealLifeServer.Domain.Enums;

/// <summary>Bit flags: a channel may accept more than one ingest protocol at once.</summary>
[Flags]
public enum IngestProtocol
{
    None = 0,
    Rtmp = 1,
    Srt = 2,
    Whip = 4
}
