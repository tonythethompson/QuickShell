using Microsoft.CommandPalette.Extensions;



using Microsoft.CommandPalette.Extensions.Toolkit;



using QuickShell.Services;







namespace QuickShell.Pages;







internal sealed partial class QuickShellExtensionSettingsPage : ContentPage



{



    public const string PageId = "com.quickshell.settings";







    private readonly QuickShellSettingsManager _settingsManager;



    private readonly Action _onReload;







    public QuickShellExtensionSettingsPage(



        QuickShellSettingsManager settingsManager,



        Action? onReload = null)



    {



        _settingsManager = settingsManager;



        _onReload = onReload ?? (() => { });



        Id = PageId;



        Name = "Settings";



        Title = "Quick Shell settings";



        Icon = new IconInfo("\uE713");

        Commands = ShortcutContextCommands.BuildUndoRedoCommands(_onReload);
    }







    public void RefreshContent() => RaiseItemsChanged();







    public override IContent[] GetContent()



    {



        var refreshSettings = (Action)RefreshContent;



        var content = new List<IContent>();







        if (QuickShellRuntimeServices.Drafts.HasPending)



        {



            content.Add(new PendingShortcutEditForm(_onReload, refreshSettings));



        }







        content.Add(new TerminalDefaultsSettingsForm(_settingsManager, _onReload, refreshSettings));



        content.Add(new ShortcutTransferSettingsForm(_onReload, refreshSettings));







        return content.ToArray();



    }



}



