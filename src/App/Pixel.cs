using Microsoft.Maui.Controls.Shapes;
using TsEncodeFlow;

namespace EncodeFlow.App;
static class Pixel {
 public static string DiagnosticsData=Integration.DataFolder;
 public static void LogError(string action,Exception error){try{Directory.CreateDirectory(DiagnosticsData);File.AppendAllText(System.IO.Path.Combine(DiagnosticsData,"ui-errors.log"),DateTime.Now.ToString("O")+" ["+action+"] "+error+Environment.NewLine);}catch{}}
 public static readonly Color Ink=Color.FromArgb("#1D252F"),Muted=Color.FromArgb("#627181"),Blue=Color.FromArgb("#185ABC"),Tint=Color.FromArgb("#D3E3FD"),Surface=Color.FromArgb("#EDF2FA");
 public static Label Text(string value,double size=14,Color? color=null,bool bold=false)=>new(){Text=value,FontSize=size,TextColor=color??Ink,FontAttributes=bold?FontAttributes.Bold:FontAttributes.None,LineBreakMode=LineBreakMode.WordWrap};
 public static Button Button(string text,Func<Task> action,bool primary=false){var b=new Button {Text=text,VerticalOptions=LayoutOptions.Center,BackgroundColor=primary?Blue:Tint,TextColor=primary?Colors.White:Color.FromArgb("#163E77")};b.Clicked+=async(s,e)=>{b.IsEnabled=false;try{await action();}catch(Exception ex){LogError(text,ex);if(Application.Current?.Windows.FirstOrDefault()?.Page is Page p)await p.DisplayAlert("確認してください",ex.Message,"閉じる");}finally{b.IsEnabled=true;}};return b;}
 public static Button Button(string text,Action action,bool primary=false)=>Button(text,()=>{action();return Task.CompletedTask;},primary);
 public static Border Card(View content,Color? color=null,double padding=24)=>new(){StrokeThickness=0,StrokeShape=new RoundRectangle {CornerRadius=24},BackgroundColor=color??Colors.White,Padding=padding,Content=content};
 public static Border Pill(string text,Color? color=null){var card=Card(Text(text,12,Blue,true),color??Tint,new Thickness(12,6));card.HorizontalOptions=LayoutOptions.Start;return card;}
 static Border Card(View content,Color color,Thickness padding)=>new(){StrokeThickness=0,StrokeShape=new RoundRectangle {CornerRadius=20},BackgroundColor=color,Padding=padding,Content=content};
 public static VerticalStackLayout Stack(params View[] children){var stack=new VerticalStackLayout {Spacing=16};foreach(var child in children)stack.Children.Add(child);return stack;}
 public static HorizontalStackLayout Row(params View[] children){var row=new HorizontalStackLayout {Spacing=12};foreach(var child in children)row.Children.Add(child);return row;}
 public static View Field(string name,View input,string? help=null){var field=Stack(Text(name,13,Muted,true),input);field.Spacing=8;if(!string.IsNullOrWhiteSpace(help))field.Children.Add(Text(help,12,Muted));return field;}
 public static Entry Entry(string text,string? placeholder=null)=>new(){Text=text,Placeholder=placeholder,ClearButtonVisibility=ClearButtonVisibility.WhileEditing};
 public static ScrollView Scroll(View view)=>new(){Content=view,VerticalScrollBarVisibility=ScrollBarVisibility.Default};
 public static string EngineTool(Settings s,Engine e)=>e==Engine.CPU?s.Ffmpeg:e==Engine.NVEncC?s.Nvenc:e==Engine.VCEEncC?s.Vceenc:s.Qsvenc;
}
