using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// 字幕・DRCS画面のデータ整形と保存処理（Windows版 DrcsView の保存経路に対応）。
// 画面と検証用スモークの両方がこの経路を通る。
static class DrcsScreenLogic {
 // Windows版と同じく、全ジョブの字形を Hash でまとめ、未対応→対応済みの順に並べる。
 public static (Dictionary<string,DrcsReplacement> Map,List<DrcsGlyph> Glyphs) Collect(EngineClientLinux client) {
  client.Refresh();
  var map=Drcs.Load(client.Data);
  var glyphs=client.Jobs.SelectMany(j=>j.Glyphs??[]).GroupBy(g=>g.Hash).Select(g=>g.First()).OrderBy(g=>Drcs.Resolved(g,map)).ToList();
  return (map,glyphs);
 }

 // 代替文字を対応表へ保存し、この字形待ちだったジョブがすべて解決したら再開を依頼する。戻り値は再開本数。
 public static int SaveReplacement(EngineClientLinux client,DrcsGlyph glyph,string text,string reference) {
  Drcs.Save(client.Data,glyph.Hash,text,reference);
  var updated=Drcs.Load(client.Data);int resumed=0;
  foreach(var job in client.Jobs.Where(j=>j.State==State.NeedsInput&&j.Glyphs.Any(g=>g.Hash==glyph.Hash)))
   if(job.Glyphs.All(g=>Drcs.Resolved(g,updated))){client.Send("retry",job.Id);resumed++;}
  return resumed;
 }

 // ジョブごとの字幕トラック情報（メタデータ由来。Windows版の字幕系表示と同じ項目）。
 public static List<(Job Job,string Info)> SubtitleInfo(EngineClientLinux client) {
  var result=new List<(Job,string)>();
  foreach(var job in client.Jobs) {
   var m=job.Metadata;if(m==null)continue;
   if(m.SubtitleIndex<0&&string.IsNullOrEmpty(m.SubtitleCodec)&&string.IsNullOrEmpty(m.SubtitleStatus))continue;
   string info="字幕："+(string.IsNullOrEmpty(m.SubtitleCodec)?"なし":m.SubtitleCodec)
    +(m.SubtitleIndex>=0?" / ストリーム "+m.SubtitleIndex:"")+(m.SubtitlePid>=0?" / PID "+m.SubtitlePid:"")
    +(m.SubtitlePackets>0?" / パケット "+m.SubtitlePackets:"")
    +(!string.IsNullOrEmpty(m.SubtitleStatus)?"\n状態："+m.SubtitleStatus:"")
    +(!string.IsNullOrEmpty(m.SubtitleWarning)?"\n警告："+m.SubtitleWarning:"")
    +((job.Glyphs?.Count??0)>0?"\nDRCS字形："+job.Glyphs.Count+"件":"");
   result.Add((job,info));
  }
  return result;
 }
}

// 字幕・DRCS画面（Windows版 DrcsView の対応物）。
sealed class DrcsScreen:UserControl {
 readonly EngineClientLinux client;readonly Action<string> notice;
 readonly StackPanel entries=new(){Spacing=18};

 public DrcsScreen(EngineClientLinux client,Action<string> notice) {
  this.client=client;this.notice=notice;
  var header=Ui.Card(Ui.Stack(
   Ui.Text("読めない文字を、読める文字に。",27,bold:true),
   Ui.Text("放送のDRCS字形を画像で確認し、参考写真と照らし合わせて代替文字を登録します。\n登録した対応表は、次の録画にも自動で適用されます。",14,muted:true),
   Ui.Row(Ui.Button("一覧を更新",Reload),
          Ui.Button("対応表を開く",()=>{Directory.CreateDirectory(Path.Combine(client.Data,"Drcs"));LinuxShell.OpenPath(Path.Combine(client.Data,"Drcs"));}))));
  var root=new StackPanel {Spacing=18};root.Children.Add(header);root.Children.Add(entries);
  Content=Ui.Scroll(root);
  Reload();
 }

 public void Reload() {
  entries.Children.Clear();
  Dictionary<string,DrcsReplacement> map;List<DrcsGlyph> glyphs;
  try{(map,glyphs)=DrcsScreenLogic.Collect(client);}catch(Exception e){notice(e.Message);return;}
  // 字幕トラック情報（ジョブ単位）
  var infos=DrcsScreenLogic.SubtitleInfo(client);
  if(infos.Count>0) {
   var stack=Ui.Stack(Ui.Text("字幕トラック情報",20,bold:true));
   foreach(var (job,info) in infos)stack.Children.Add(Ui.Stack(Ui.Text(job.Metadata?.Title??Path.GetFileName(job.Input),15,bold:true),Ui.Text(info,13,muted:true)));
   entries.Children.Add(Ui.Card(stack));
  }
  if(glyphs.Count==0) {
   entries.Children.Add(Ui.Card(Ui.Stack(
    Ui.Text("未確認のDRCSはありません",21,bold:true),
    Ui.Text("ARIB字幕付きTSを変換すると、検出した字形がここに並びます。\n字幕が存在しない録画やDRCSを含まない字幕では一覧は空になります。",14,muted:true))));
   return;
  }
  foreach(var glyph in glyphs)entries.Children.Add(GlyphCard(glyph,map));
 }

 Control GlyphCard(DrcsGlyph glyph,Dictionary<string,DrcsReplacement> map) {
  map.TryGetValue(glyph.Hash,out var previous);string reference=previous?.ReferenceImage??"";
  var replacement=Ui.Entry(previous?.Text??glyph.Alternative,"例：髙、﨑、☎ …");replacement.FontSize=26;
  var glyphImage=new Image {Height=190,Stretch=Stretch.Uniform};
  var glyphBitmap=LogoBitmapImage.LoadFile(glyph.Image);if(glyphBitmap!=null)glyphImage.Source=glyphBitmap;
  RenderOptions.SetBitmapInterpolationMode(glyphImage,BitmapInterpolationMode.None);
  var photo=new Image {Height=190,Stretch=Stretch.Uniform};
  var photoBitmap=LogoBitmapImage.LoadFile(reference);if(photoBitmap!=null)photo.Source=photoBitmap;
  var images=new Grid {ColumnDefinitions=new ColumnDefinitions("*,*"),ColumnSpacing=18};
  var left=Ui.Card(Ui.Stack(Ui.Text("放送された字形",13,bold:true,muted:true),glyphImage));
  var rightStack=Ui.Stack(Ui.Text("参考写真",13,bold:true,muted:true),photo,
   Ui.Button("写真・スクリーンショットを選ぶ",async()=> {
    try {
     var files=await PickImages();if(files.Count==0)return;string source=files[0];
     if(new FileInfo(source).Length>30*1024*1024)throw new ArgumentException("30MB以下の画像を選択してください。");
     string dir=Path.Combine(client.Data,"Drcs","References");Directory.CreateDirectory(dir);
     reference=Path.Combine(dir,Guid.NewGuid().ToString("N")+Path.GetExtension(source));File.Copy(source,reference);
     var bitmap=LogoBitmapImage.LoadFile(reference);if(bitmap!=null)photo.Source=bitmap;
    }catch(Exception e){notice(e.Message);}
   }));
  var right=Ui.Card(rightStack);
  Grid.SetColumn(left,0);Grid.SetColumn(right,1);images.Children.Add(left);images.Children.Add(right);
  bool resolved=Drcs.Resolved(glyph,map);
  var note=Ui.Text(resolved?"登録済み／既知の代替文字があります":"未対応 · 代替文字を入力してください",13,bold:true);
  note.Foreground=new SolidColorBrush(Color.Parse(resolved?"#167349":"#A45E00"));
  var save=Ui.Button("保存して待機中の変換を再開",() => {
   try {
    int resumed=DrcsScreenLogic.SaveReplacement(client,glyph,replacement.Text??"",reference);
    note.Text="対応表に保存しました";note.Foreground=new SolidColorBrush(Color.Parse("#167349"));
    notice(resumed>0?resumed+"本の変換を再開します。":"保存しました。他にも未対応の字形がある場合は続けて入力してください。");
   }catch(Exception e){notice(e.Message);}
  },bold:true);
  return Ui.Card(Ui.Stack(note,images,
   Ui.Field("この字形に対応する文字",replacement,"自動OCRは行いません。見比べて確認した文字を保存してください。"),
   Ui.Text("字幕の前後："+glyph.Context,13,muted:true),
   Ui.Text("字形ID："+glyph.Hash,11,muted:true),save));
 }

 async Task<IReadOnlyList<string>> PickImages() {
  var top=TopLevel.GetTopLevel(this);if(top==null)return [];
  var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
   Title="参考写真を選択",AllowMultiple=false,
   FileTypeFilter=[new FilePickerFileType("画像"){Patterns=["*.png","*.jpg","*.jpeg","*.bmp","*.webp"]}]});
  return files.Select(f=>f.TryGetLocalPath()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToList();
 }
}
