using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public static class MetadataReader {
  public static string[] ProbeInput()=>new[]{"-probesize","20000000","-analyzeduration","10000000"};
  public static int Pid(Dictionary<string,object> stream){string value=Text(stream,"id");return value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)&&int.TryParse(value.Substring(2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int pid)?pid:0;}
  public static List<Dictionary<string,object>> DecodableAudio(IEnumerable<Dictionary<string,object>> streams,Action<string> log) {
   var advertised=streams.Where(x=>Text(x,"codec_type")=="audio").ToList();var result=new List<Dictionary<string,object>>();foreach(var stream in advertised) {
    if(Int(stream,"channels")<=0||Int(stream,"sample_rate")<=0||string.IsNullOrWhiteSpace(Text(stream,"codec_name"))){log("音声ストリーム "+Int(stream,"index")+"（PID "+Pid(stream)+"）はチャンネル数・サンプルレート等を取得できないため、自動選択から除外します。");continue;}result.Add(stream);
   }if(advertised.Count>0&&result.Count==0)throw new InvalidDataException("音声ストリームはありますが、復号に必要なチャンネル数・サンプルレートを取得できません。入力の受信エラーとログを確認してください。");return result;
  }
  public static Dictionary<string,object> Obj(object value)=>value as Dictionary<string,object>??new Dictionary<string,object>();
  public static IEnumerable<object> Items(Dictionary<string,object> value,string key){object v;return value.TryGetValue(key,out v)&&v is IEnumerable&&!(v is string)?((IEnumerable)v).Cast<object>():Enumerable.Empty<object>();}
  public static string Text(Dictionary<string,object> value,string key,string fallback=""){object v;return value.TryGetValue(key,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):fallback;}
  public static int Int(Dictionary<string,object> value,string key){int n;return int.TryParse(Text(value,key),out n)?n:0;}
  public static double Number(string text){double n;return double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)?n:0;}
  public static double Ratio(string text){var s=text.Split(':','/');return s.Length==2?Number(s[0])/Math.Max(Number(s[1]),0.000001):Number(text);}
  public static async Task<Metadata> Read(string file,Settings settings,int serviceId,CancellationToken token,Action<string> log) {
   var probe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-probesize","20000000","-analyzeduration","10000000","-show_programs","-show_streams","-show_format","-of","json",file},token,null,60);
   if(probe.ExitCode!=0)throw new InvalidDataException("ffprobeで入力を解析できません："+probe.Error);
   var data=Store.Json.Deserialize<Dictionary<string,object>>(probe.Output);
   var streams=Items(data,"streams").Select(Obj).ToList();var programs=Items(data,"programs").Select(Obj).ToList();
   string sidecar=new[]{file+".program.txt",Path.ChangeExtension(file,".program.txt")}.FirstOrDefault(File.Exists);
   var sideInfo=new Metadata();if(sidecar!=null)ReadSidecar(sidecar,sideInfo);
   int selectedId=serviceId!=0?serviceId:sideInfo.ServiceId;
   var program=selectedId==0?null:programs.FirstOrDefault(x=>Int(x,"program_id")==selectedId);
   if(selectedId!=0&&program==null)throw new InvalidDataException("指定または録画番組情報のサービスIDがTSにありません："+selectedId);
   if(program!=null)streams=Items(program,"streams").Select(Obj).ToList();
   var video=streams.Where(s=>Text(s,"codec_type")=="video").OrderByDescending(s=>(long)Int(s,"width")*Int(s,"height")).FirstOrDefault();
   if(video==null||Int(video,"width")<2||Int(video,"height")<2)throw new InvalidDataException("有効な映像がありません。");
   if(program==null)program=programs.FirstOrDefault(p=>Items(p,"streams").Select(Obj).Any(s=>Int(s,"index")==Int(video,"index")));
   if(program!=null)streams=Items(program,"streams").Select(Obj).ToList();
   var validAudio=DecodableAudio(streams,log);var audio=validAudio.FirstOrDefault();
   var subtitle=streams.FirstOrDefault(s=>Text(s,"codec_type")=="subtitle");
   var m=new Metadata {OriginalName=Path.GetFileNameWithoutExtension(file),Start=File.GetLastWriteTime(file),Width=Int(video,"width"),Height=Int(video,"height"),VideoIndex=Int(video,"index"),AudioIndex=audio==null?-1:Int(audio,"index"),ServiceId=program==null?0:Int(program,"program_id"),Sar=Ratio(Text(video,"sample_aspect_ratio","1:1")),Fps=Ratio(Text(video,"r_frame_rate")),Duration=Number(Text(video,"duration")),Source="ファイル名・更新時刻（番組日時が取得できない場合の代替）"};
   m.TransportId=Int(video,"ts_id");m.PcrPid=program!=null&&int.TryParse(Text(program,"pcr_pid"),out int pcr)?pcr:-1;
   m.ColorSpace=Text(video,"color_space");m.ColorRange=Text(video,"color_range");m.ColorPrimaries=Text(video,"color_primaries");m.ColorTransfer=Text(video,"color_transfer");m.PixelFormat=Text(video,"pix_fmt");m.FieldOrder=Text(video,"field_order");
   m.AudioTracks=validAudio.Select(x=>{var tags=Obj(x.ContainsKey("tags")?x["tags"]:null);var disposition=Obj(x.ContainsKey("disposition")?x["disposition"]:null);return new AudioTrack {Index=Int(x,"index"),Pid=Pid(x),Channels=Int(x,"channels"),Codec=Text(x,"codec_name"),Language=Text(tags,"language","und"),Title=Text(tags,"title"),Layout=Text(x,"channel_layout"),Default=Int(disposition,"default")!=0,Commentary=Int(disposition,"comment")!=0,Start=Number(Text(x,"start_time"))};}).ToList();
   if(m.Sar<=0||double.IsNaN(m.Sar)||double.IsInfinity(m.Sar))m.Sar=1;
   m.VideoStart=Number(Text(video,"start_time"));m.AudioStart=audio==null?m.VideoStart:Number(Text(audio,"start_time"));m.FormatStart=Number(Text(Obj(data.ContainsKey("format")?data["format"]:null),"start_time"));
   string videoPid=Text(video,"id");if(videoPid.StartsWith("0x")&&int.TryParse(videoPid.Substring(2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int pidValue))m.VideoPid=pidValue;
   if(subtitle!=null){m.SubtitleIndex=Int(subtitle,"index");m.SubtitleCodec=Text(subtitle,"codec_name");string pid=Text(subtitle,"id");try{m.SubtitlePid=pid.StartsWith("0x")?Convert.ToInt32(pid.Substring(2),16):int.Parse(pid);}catch(FormatException){}}
   if(m.Duration<=0)m.Duration=Number(Text(Obj(data.ContainsKey("format")?data["format"]:null),"duration"));
   if(File.Exists(settings.EpgDll))try {
    long middle=-1;
    bool useSidecar=sidecar!=null&&(sideInfo.ServiceId==0||sideInfo.ServiceId==m.ServiceId)&&!string.IsNullOrWhiteSpace(sideInfo.Title);
    if(!useSidecar&&m.Duration>2)try {
     var sample=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-read_intervals",Commercials.N(m.Duration/2)+"%+1","-select_streams",m.VideoIndex.ToString(),"-show_packets","-show_entries","packet=pos","-of","csv=p=0",file},token,null,30);
     if(sample.ExitCode==0)foreach(string line in sample.Output.Split('\n'))if(long.TryParse(line.Split(',')[0].Trim(),out long position)&&position>=0){middle=position;break;}
    }catch(Exception e)when(e is not OperationCanceledException){log("録画中央の番組情報取得位置を特定できません："+e.Message);}
    NativeEpg.Fill(file,settings.EpgDll,m,token,middle,log);log("TS番組情報："+m.Title+" / "+m.Channel);
   }catch(Exception e)when(e is not OperationCanceledException){log("TS番組情報を取得できません："+e.Message);}
   else log("EpgDataCap3.dllが未指定です。.program.txtがあれば利用します。");
   if(sidecar!=null&&(sideInfo.ServiceId==0||sideInfo.ServiceId==m.ServiceId)){ReadSidecar(sidecar,m);log("録画番組情報："+Path.GetFileName(sidecar));}
   else if(sidecar!=null)log(".program.txtと選択SIDが異なるため、番組名はTS内の情報を利用します。");
   if(string.IsNullOrWhiteSpace(m.Title))m.Title=m.OriginalName;
   var episode=Regex.Match(m.Title,@"(?:第\s*|[#＃])([0-9０-９]+)\s*(?:話|回)?");m.Episode=episode.Success?episode.Groups[1].Value.Normalize(NormalizationForm.FormKC):"";
   return m;
  }
  public static void ReadSidecar(string file,Metadata m) {
   byte[] bytes=File.ReadAllBytes(file);string text;
   if(bytes.Length>=2&&((bytes[0]==255&&bytes[1]==254)||(bytes[0]==254&&bytes[1]==255)))text=(bytes[0]==255?Encoding.Unicode:Encoding.BigEndianUnicode).GetString(bytes,2,bytes.Length-2);
   else{try{text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\ufeff');}catch(DecoderFallbackException){text=Encoding.GetEncoding(932).GetString(bytes);}}
   var lines=text.Replace("\r\n","\n").Split('\n');
   var match=Regex.Match(text,@"(?m)^(\d{4}/\d{1,2}/\d{1,2})\([^\r\n]*?\)\s+(\d{1,2}:\d{2}(?::\d{2})?)");
   if(match.Success) {
    DateTime start;if(DateTime.TryParse(match.Groups[1].Value+" "+match.Groups[2].Value,CultureInfo.InvariantCulture,DateTimeStyles.None,out start))m.Start=start;
    if(lines.Length>1&&!string.IsNullOrWhiteSpace(lines[1]))m.Channel=lines[1].Trim();
    if(lines.Length>2&&!string.IsNullOrWhiteSpace(lines[2]))m.Title=lines[2].Trim();
    m.Source="EDCB .program.txt（日時・局・番組名を優先） / "+m.Source;
   }
   m.Description=text;
   foreach(var pair in new[]{"OriginalNetworkID","TransportStreamID","ServiceID","EventID"}){var id=Regex.Match(text,Regex.Escape(pair)+@":\s*(\d+)");if(!id.Success)continue;int value=int.Parse(id.Groups[1].Value);if(pair=="OriginalNetworkID")m.NetworkId=value;else if(pair=="TransportStreamID")m.TransportId=value;else if(pair=="ServiceID")m.ServiceId=value;else m.EventId=value;}
  }
 }
 public static class NativeEpg {
  static readonly object gate=new object();
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
  [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Init(int asyncFlag,out uint id);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Uninit(uint id);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Add(uint id,byte[] data,uint size);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Services(uint id,out uint count,out IntPtr list);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Events(uint id,ushort onid,ushort tsid,ushort sid,out uint count,out IntPtr list);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Event(uint id,ushort onid,ushort tsid,ushort sid,int next,out IntPtr info);
  [StructLayout(LayoutKind.Sequential)]struct Service {public ushort Onid,Tsid,Sid;public IntPtr Ext;}
  [StructLayout(LayoutKind.Sequential)]struct ServiceExt {public byte Type,Partial;public IntPtr Provider,Name,Network,Ts;public byte Remote;}
  [StructLayout(LayoutKind.Sequential)]struct SysTime {public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Ms;}
  [StructLayout(LayoutKind.Sequential)]struct EventInfo {public ushort Id;public byte StartFlag;public SysTime Start;public byte DurationFlag;public uint Duration;public IntPtr Short,Ext,Content,Component,Audio,Group,Relay;public byte FreeCA;}
  [StructLayout(LayoutKind.Sequential)]struct Short {public ushort NameLength;public IntPtr Name;public ushort TextLength;public IntPtr Text;}
  static T Bind<T>(IntPtr module,string name)where T:class {var p=GetProcAddress(module,name);if(p==IntPtr.Zero)throw new InvalidDataException("EDCB DLLにAPIがありません："+name);return Marshal.GetDelegateForFunctionPointer(p,typeof(T))as T;}
  public static void Fill(string file,string dll,Metadata m,CancellationToken token,long middlePosition=-1,Action<string> log=null) {
   token.ThrowIfCancellationRequested();
   lock(gate) {
    token.ThrowIfCancellationRequested();
    IntPtr module=LoadLibraryEx(Path.GetFullPath(dll),IntPtr.Zero,0x100|0x1000);if(module==IntPtr.Zero)throw new InvalidDataException("EDCB x64 DLLを読み込めません（"+Marshal.GetLastWin32Error()+"）。");uint id=0;Uninit uninit=null;bool initialized=false;
    try {
     var init=Bind<Init>(module,"InitializeEP");uninit=Bind<Uninit>(module,"UnInitializeEP");var add=Bind<Add>(module,"AddTSPacketEP");var services=Bind<Services>(module,"GetServiceListActualEP");var current=Bind<Event>(module,"GetEpgInfoEP");
     var listProc=GetProcAddress(module,"GetEpgInfoListEP");var events=listProc==IntPtr.Zero?null:Marshal.GetDelegateForFunctionPointer<Events>(listProc);
     if(init(0,out id)!=1)throw new InvalidDataException("EPG解析の初期化に失敗。");initialized=true;
     var clock=new EpgClock(m.PcrPid>=0?m.PcrPid:m.VideoPid);
     using var input=File.OpenRead(file);
     void Scan(long position,long limit,bool anchor) {
      input.Position=Math.Clamp(position,0,input.Length);long read=0;byte[] packet=new byte[188];
      while(read<limit&&input.Read(packet,0,188)==188) {
       read+=188;if(read%(188*1024)==0)token.ThrowIfCancellationRequested();
       if(packet[0]!=0x47){input.Position-=187;continue;}
       if(anchor)clock.Add(packet);
       int pid=((packet[1]&31)<<8)|packet[2];if(pid<32)add(id,packet,188);
      }
     }
     Scan(0,64*1024*1024,true);
     uint count;IntPtr list;
     if(services(id,out count,out list)!=1||count==0) {
      var fallback=GetProcAddress(module,"GetServiceListEpgDBEP");
      if(fallback==IntPtr.Zero||Marshal.GetDelegateForFunctionPointer<Services>(fallback)(id,out count,out list)!=1)return;
     }
     if(count>10000)return;
     Service? selected=null;
     for(int i=0;i<count;i++) {
      var service=Marshal.PtrToStructure<Service>(IntPtr.Add(list,i*Marshal.SizeOf<Service>()));if(m.ServiceId!=0&&service.Sid!=m.ServiceId||m.TransportId!=0&&service.Tsid!=m.TransportId)continue;
      selected=service;m.NetworkId=service.Onid;m.TransportId=service.Tsid;m.ServiceId=service.Sid;
      if(service.Ext!=IntPtr.Zero){var ext=Marshal.PtrToStructure<ServiceExt>(service.Ext);m.Channel=Marshal.PtrToStringUni(ext.Name)??"";}break;
     }
     if(!selected.HasValue)return;var target=selected.Value;var candidates=new List<EpgCandidate>();
     EpgCandidate Copy(IntPtr pointer) {
      if(pointer==IntPtr.Zero)return null;var ev=Marshal.PtrToStructure<EventInfo>(pointer);var candidate=new EpgCandidate {EventId=ev.Id,Duration=ev.DurationFlag!=0?ev.Duration:0};
      if(ev.StartFlag!=0)try{candidate.Start=new DateTime(ev.Start.Year,ev.Start.Month,ev.Start.Day,ev.Start.Hour,ev.Start.Minute,ev.Start.Second);}catch(ArgumentOutOfRangeException){}
      if(ev.Short!=IntPtr.Zero){var info=Marshal.PtrToStructure<Short>(ev.Short);candidate.Title=Marshal.PtrToStringUni(info.Name)??"";candidate.Description=Marshal.PtrToStringUni(info.Text)??"";}
      if(!string.IsNullOrWhiteSpace(candidate.Title))candidates.Add(candidate);return candidate;
     }
     EpgCandidate Collect() {
      if(events!=null&&events(id,target.Onid,target.Tsid,target.Sid,out uint size,out IntPtr data)==1&&size<=100000)for(int i=0;i<size;i++)Copy(IntPtr.Add(data,i*Marshal.SizeOf<EventInfo>()));
      EpgCandidate present=null;
      for(int next=0;next<=1;next++)if(current(id,target.Onid,target.Tsid,target.Sid,next,out IntPtr ptr)==1){var item=Copy(ptr);if(next==0)present=item;}
      return present;
     }
     var beginning=Collect();EpgCandidate middle=null;
     if(middlePosition>=0&&middlePosition<input.Length){Scan(middlePosition,16*1024*1024,false);middle=Collect();}
     var chosen=EpgSelection.Choose(candidates,clock.RecordingStart,m.Duration,middle,beginning);
     if(chosen==null)return;
     m.Title=chosen.Title;m.Description=chosen.Description;m.EventId=chosen.EventId;if(chosen.Start!=default)m.Start=chosen.Start;
     m.Source=EpgSelection.Overlap(chosen,clock.RecordingStart,m.Duration)>0?"TS内のEIT/SDT（放送時計・録画範囲との重複時間で番組を選択）":!string.IsNullOrWhiteSpace(middle?.Title)?"TS内のEIT/SDT（重複時間を判定できないため録画中央の番組を選択）":"TS内のEIT/SDT（重複時間・録画中央を判定できないため冒頭の番組へフォールバック）";
     log?.Invoke("番組情報の選択："+m.Title+" / "+m.Source+(clock.RecordingStart.HasValue?" / 録画先頭の放送時刻 "+clock.RecordingStart.Value.ToString("yyyy/MM/dd HH:mm:ss"):""));
    }finally{if(initialized)uninit(id);FreeLibrary(module);}
   }
  }
 }
}
