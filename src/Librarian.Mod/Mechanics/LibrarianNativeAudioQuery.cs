using Godot;

namespace Librarian.Mechanics;

/// <summary>Read-only probes of the game's loaded FMOD server; playback stays on SfxCmd.</summary>
internal static class LibrarianNativeAudioQuery
{
    private static GodotObject? Server => Engine.HasSingleton("FmodServer")
        ? Engine.GetSingleton("FmodServer") : null;

    internal static bool EventExists(string path) => Exists("check_event_path", path);
    internal static bool BusExists(string path) => Exists("check_bus_path", path);
    private static bool Exists(string method, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return Server is { } server && GodotObject.IsInstanceValid(server)
                && server.HasMethod(method) && server.Call(method, path).AsBool();
        }
        catch { return false; }
    }

    internal static float GetBusVolume(string path)
    {
        if (Server is not { } server || !GodotObject.IsInstanceValid(server))
            throw new InvalidOperationException("Native FMOD server is unavailable.");
        var bus = server.Call("get_bus", path).AsGodotObject();
        if (bus is null || !GodotObject.IsInstanceValid(bus))
            throw new InvalidOperationException("Native FMOD bus is unavailable: " + path);
        return bus.Call("get_volume").AsSingle();
    }
}
