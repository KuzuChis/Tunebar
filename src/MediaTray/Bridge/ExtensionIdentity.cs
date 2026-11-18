namespace MediaTray.Bridge;

/// <summary>Fixed values shared by the tray app and the Opera extension.</summary>
public static class ExtensionIdentity
{
    /// <summary>Extension Id derived from the public key in the extension manifest.</summary>
    public const string Id = "gfcfaloonenbemggdkmocjkeeddkadba";

    /// <summary>Local port the extension connects to.</summary>
    public const int Port = 47821;
}
