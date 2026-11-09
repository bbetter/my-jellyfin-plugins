using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyShare;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    // Marker attribute so we never double-inject across restarts
    private const string ScriptTag =
        "<script src=\"/JellyShare/ClientScript\" data-jellyshare-plugin defer></script>";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        InjectWebScript(applicationPaths.WebPath);
    }

    public override string Name => "JellyShare";

    public override Guid Id => Guid.Parse("3a7b2c1d-4e5f-6a7b-8c9d-0e1f2a3b4c5d");

    public override string Description => "Generate temporary public share links for media items.";

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages() =>
        new[] { new PluginPageInfo { Name = "JellyShare", EmbeddedResourcePath = "JellyShare.Resources.admin.html" } };

    private static void InjectWebScript(string webPath)
    {
        try
        {
            var indexPath = Path.Combine(webPath, "index.html");
            if (!File.Exists(indexPath)) return;

            var content = File.ReadAllText(indexPath);
            if (content.Contains("data-jellyshare-plugin")) return; // already injected

            // Insert just before </head> so it loads on every page
            var patched = content.Replace(
                "</head>",
                ScriptTag + "\n</head>",
                StringComparison.OrdinalIgnoreCase);

            if (patched != content)
                File.WriteAllText(indexPath, patched);
        }
        catch
        {
            // Non-fatal: Jellyfin may be running read-only; admin can inject manually.
        }
    }
}
