using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class SettingsView:ContentView {
 public SettingsView(EngineClient client,Action<string> notice) {
  var s=client.Settings;Entry output=Pixel.Entry(s.OutputFolder),macro=Pixel.Entry(s.FileName);var preview=Pixel.Text("",14,Pixel.Blue,true);
  void Preview(){try{preview.Text="出力例："+Names.Render(macro.Text??"",new Metadata {Title="お気に入りの番組 第12話",Channel="テスト放送",OriginalName="録画",Start=DateTime.Today.AddHours(21),Episode="12",Width=1920,Height=1080},client.Selected);}catch(Exception e){preview.Text=e.Message;}}
  macro.TextChanged+=(a,b)=>Preview();Preview();
  var toolInputs=new Dictionary<string,Entry>();
  var tools=Pixel.Stack(Pixel.Text("変換ツール",22,null,true),Pixel.Text("FFmpegとffprobeはすべての方式で必要です。\n日本語番組名のTS解析には、EDCB x64のEpgDataCap3.dllを指定できます。",13,Pixel.Muted));
  foreach(var pair in new[]{("Ffmpeg","FFmpeg · 必須"),("Ffprobe","ffprobe · 必須"),("Nvenc","NVEncC · NVIDIA"),("Vceenc","VCEEncC · AMD"),("Qsvenc","QSVEncC · Intel"),("EpgDll","EpgDataCap3.dll · 番組情報")}) {
   string key=pair.Item1;var property=typeof(Settings).GetProperty(key)!;var entry=Pixel.Entry((string)property.GetValue(s)!);toolInputs[key]=entry;
   var grid=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Auto)},ColumnSpacing=12};grid.Add(entry,0,0);grid.Add(Pixel.Button("選択",async()=>{var files=await Pickers.Files(key=="EpgDll"?".dll":".exe");if(files.Count>0)entry.Text=files[0];}),1,0);tools.Children.Add(Pixel.Field(pair.Item2,grid));
  }
  var links=new FlexLayout {Wrap=Microsoft.Maui.Layouts.FlexWrap.Wrap};foreach(var pair in new[]{("FFmpeg公式","https://ffmpeg.org/download.html"),("NVEncC","https://github.com/rigaya/NVEnc/releases"),("VCEEncC","https://github.com/rigaya/VCEEnc/releases"),("QSVEncC","https://github.com/rigaya/QSVEnc/releases")}){string url=pair.Item2;var b=Pixel.Button(pair.Item1,()=>Pickers.Open(url));b.Margin=new Thickness(0,0,8,8);links.Children.Add(b);}tools.Children.Add(links);
  var save=Pixel.Button("設定を保存",()=> {
   foreach(var pair in toolInputs)typeof(Settings).GetProperty(pair.Key)!.SetValue(s,pair.Value.Text??"");s.OutputFolder=output.Text??"";s.FileName=macro.Text??"";client.Save();notice("保存先・ファイル名・ツールの設定を保存しました。");
  },true);
  Content=Pixel.Scroll(Pixel.Stack(Pixel.Card(Pixel.Stack(Pixel.Text("保存先とファイル名",22,null,true),Pixel.Field("動画の保存先",output),Pixel.Button("保存先を選択",async()=>{string? path=await Pickers.Folder();if(path!=null)output.Text=path;}),Pixel.Field("ファイル名マクロ",macro,"{Title} {Channel} {Start:yyyyMMdd_HHmm} {Episode} {OriginalName} {ServiceId} {EventId} {Codec} {Engine}"),preview)),Pixel.Card(tools),save,Pixel.Card(Pixel.Stack(Pixel.Text("設定・ログ",20,null,true),Pixel.Text(client.Data,12,Pixel.Muted),Pixel.Button("設定フォルダーを開く",()=>Pickers.Open(client.Data))))));
 }
}
