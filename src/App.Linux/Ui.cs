using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EncodeFlow.App.Linux;

// Windows版 Pixel（src/App）に相当する、コード構築UIの小さなヘルパー群。
// B-2の各画面は MAUI 版と同じくコードで組み立てる（フィールド数が多く、直接読み書きが確実なため）。
static class Ui {
 public static readonly IBrush CardBorder=new SolidColorBrush(Color.Parse("#D0D7E3"));
 public static readonly IBrush PillBack=new SolidColorBrush(Color.Parse("#EDF2FA"));
 public static readonly IBrush SelectedBack=new SolidColorBrush(Color.Parse("#E8F0FE"));

 public static TextBlock Text(string text,int size=14,bool bold=false,bool muted=false) {
  var t=new TextBlock {Text=text,FontSize=size,FontWeight=bold?FontWeight.Bold:FontWeight.Normal,TextWrapping=TextWrapping.Wrap};
  if(muted)t.Foreground=Brushes.Gray;
  return t;
 }
 public static TextBlock Note(string text)=>Text(text,12,muted:true);
 public static Border Card(Control child,bool selected=false)=>new() {
  BorderBrush=CardBorder,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),
  Padding=new Thickness(14),Background=selected?SelectedBack:Brushes.White,Child=child};
 public static StackPanel Stack(params Control[] children) {
  var s=new StackPanel {Spacing=10};
  foreach(var c in children)s.Children.Add(c);
  return s;
 }
 public static StackPanel Row(params Control[] children) {
  var s=new StackPanel {Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center};
  foreach(var c in children)s.Children.Add(c);
  return s;
 }
 public static StackPanel Field(string label,Control control,string? note=null) {
  var s=new StackPanel {Spacing=4};
  s.Children.Add(Text(label,12,muted:true));s.Children.Add(control);
  if(note!=null)s.Children.Add(Note(note));
  return s;
 }
 public static TextBox Entry(string text="",string? watermark=null)=>new() {Text=text,Watermark=watermark,MinWidth=220};
 public static ComboBox Picker(IReadOnlyList<string> items,int index)=>new() {ItemsSource=items,SelectedIndex=Math.Clamp(index,0,items.Count-1),MinWidth=280};
 public static Button Button(string text,Action onClick,bool bold=false) {
  var b=new Button {Content=text};
  if(bold)b.FontWeight=FontWeight.Bold;
  b.Click+=(_,_)=>onClick();
  return b;
 }
 public static Border Pill(string text)=>new() {Background=PillBack,CornerRadius=new CornerRadius(8),Padding=new Thickness(8,2,8,2),Child=Text(text,11)};
 public static ScrollViewer Scroll(Control child)=>new() {Content=child};
}
