using System;

namespace PackFrame;

internal static class ModVisibility
{
    // Single rule for "may this instance show/use this mod"; the pane filter and drop guard share it.
    internal static bool IsVisible(ModInfo mod, ManagedInstance? instance)
    {
        if (instance is null || instance.IsOfficial || instance.IsScratch)
        {
            return !mod.IsProfileMods;
        }

        var sameProfile = string.Equals(
            mod.ProfileFolder,
            instance.FolderName,
            StringComparison.OrdinalIgnoreCase
        );
        if (instance.IsSteamCmd)
        {
            return mod.Source == ModSource.Official || (mod.IsProfileMods && sameProfile);
        }

        return !mod.IsProfileMods || sameProfile;
    }
}
