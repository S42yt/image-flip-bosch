using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;
using System;
using System.Collections.Generic;
using System.Text;

namespace image_flip_bosch.CLI.TUI
{
  public class MemeCreationScreen
  {
    private readonly ConsoleWindowSystem _windowSystem;

    private readonly Window _parent;

    private  PromptControl _searchInput;

    private  PromptControl _topTextInput;

    private  PromptControl _bottomTextInput;

    private  PromptControl _fontSizeInput;

 

    private CheckboxControl _autoPreview;

    private CheckboxControl _noWatermark;

 

    private MarkupControl _templateList;

    private MarkupControl _preview;

    private MarkupControl _templateInfo;

    private MarkupControl _status;
    private readonly Window _window;


    public MemeCreationScreen(ConsoleWindowSystem windowSystem, Window windows)
    {
      _windowSystem   = windowSystem;
      _parent = windows;


      _window = createWindow();
    }

    public void Show()
    {
      _windowSystem.AddWindow(_window);
    }

    private Window createWindow()
    {
      _searchInput = Controls.Prompt(" Search ")
                             .WithPlaceholder("Template or GIF suchen")
                             .UnfocusOnEnter(false)
                             .Build();
      



      _topTextInput = Controls.Prompt(" Top text ")
                              .WithPlaceholder("Text oben")
                              .UnfocusOnEnter(false)
                              .Build();
      



      _bottomTextInput = Controls.Prompt(" Bottom text ")
                                 .WithPlaceholder("Text unten")
                                 .UnfocusOnEnter(false)
                                 .Build();
      



      _fontSizeInput = Controls.Prompt(" Font size ")
                               .WithPlaceholder("50")
                               .UnfocusOnEnter(false)
                               .Build();
      



      _autoPreview = Controls.Checkbox("Live preview")
      .Checked(true)
      .Build();
      



      _noWatermark = Controls.Checkbox("Remove watermark")
      .Checked(false)
      .Build();

      _templateList = Controls.Markup("").Build();



      _preview = Controls.Markup("\"[dim]Keine Vorschau geladen.[/] \n [dim]Wähle links ein Meme-Template aus.[/]").Build();
      



      _templateInfo = Controls.Markup("[dim]Kein Template ausgewählt[/]")

      .Build();
      



      _status = Controls.Markup("[cyan]Ready[/]")

      .Build();
      



      //HorizontalGridControl mainGrid = CreateMainGrid();
      
      HorizontalGridControl actionBar = CreateActionBar();


      var window = new WindowBuilder(_windowSystem)
                        .WithTitle("Meme Erstellung")
                        .WithSize(64, 14)
                        .Centered()
                        .AsModal()
                        .Resizable(true)
                        .Minimizable(true)
                        .Maximizable(true)
                        .Maximized()
                        .AddControls(
                             CreateHeader()
                             )
                        .Build();
      window.PreviewKeyPressed += (_, e) =>
      {
        if(e.KeyInfo.Key == ConsoleKey.Escape)
        {
          window.Close();
          e.Handled = true;
        }
      };

      return window;
    }

    private HorizontalGridControl CreateActionBar()
    {
      throw new NotImplementedException();
    }

    private MarkupControl CreateHeader()
    {
      return Controls.Markup(" Select • Preview • Create[/]  [dim]Images and animated GIF templates[/]").Build();
    }

}
}
