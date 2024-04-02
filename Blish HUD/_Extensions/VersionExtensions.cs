using System;

namespace Blish_HUD {
    internal static class VersionExtensions {

        public static string BaseAndPrerelease(this SemanticVersioning.Version version) {
            return version.ToString().Split(new char[]{ '+' }, StringSplitOptions.RemoveEmptyEntries)[0];
        }

    }
}
