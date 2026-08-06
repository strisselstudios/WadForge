using WadForge.Core;

namespace WadForge.Wad;

public interface IWadArchiveCodec
{
    WadFormat Format { get; }
}
