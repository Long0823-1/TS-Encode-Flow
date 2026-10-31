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
   var audio=streams.FirstOrDefault(s=>Text(s,"codec_type")=="audio");
   var subtitle=streams.FirstOrDefault(s=>Text(s,"codec_type")=="subtitle");
   var m=new Metadata {OriginalName=Path.GetFileNameWithoutExtension(file),Start=File.GetLastWriteTime(file),Width=Int(video,"width"),Height=Int(video,"height"),VideoIndex=Int(video,"index"),AudioIndex=audio==null?-1:Int(audio,"index"),ServiceId=program==null?0:Int(program,"program_id"),Sar=Ratio(Text(video,"sample_aspect_ratio","1:1")),Fps=Ratio(Text(video,"r_frame_rate")),Duration=Number(Text(video,"duration")),Source="ファイル名・更新時刻（番組日時が取得できない場合の代替）"};
   if(m.Sar<=0||double.IsNaN(m.Sar)||double.IsInfinity(m.Sar))m.Sar=1;
   m.VideoStart=Number(Text(video,"start_time"));m.AudioStart=audio==null?m.VideoStart:Number(Text(audio,"start_time"));
   string videoPid=Text(video,"id");if(videoPid.StartsWith("0x")&&int.TryParse(videoPid.Substring(2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int pidValue))m.VideoPid=pidValue;
   if(subtitle!=null){m.SubtitleIndex=Int(subtitle,"index");m.SubtitleCodec=Text(subtitle,"codec_name");string pid=Text(subtitle,"id");try{m.SubtitlePid=pid.StartsWith("0x")?Convert.ToInt32(pid.Substring(2),16):int.Parse(pid);}catch(FormatException){}}
   if(m.Duration<=0)m.Duration=Number(Text(Obj(data.ContainsKey("format")?data["format"]:null),"duration"));
   if(File.Exists(settings.EpgDll))try {NativeEpg.Fill(file,settings.EpgDll,m,token);log("TS番組情報："+m.Title+" / "+m.Channel);}catch(Exception e){log("TS番組情報を取得できません："+e.Message);}
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
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate uint Event(uint id,ushort onid,ushort tsid,ushort sid,int next,out IntPtr info);
  [StructLayout(LayoutKind.Sequential)]struct Service {public ushort Onid,Tsid,Sid;public IntPtr Ext;}
  [StructLayout(LayoutKind.Sequential)]struct ServiceExt {public byte Type,Partial;public IntPtr Provider,Name,Network,Ts;public byte Remote;}
  [StructLayout(LayoutKind.Sequential)]struct SysTime {public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Ms;}
  [StructLayout(LayoutKind.Sequential)]struct EventInfo {public ushort Id;public byte StartFlag;public SysTime Start;public byte DurationFlag;public uint Duration;public IntPtr Short,Ext,Content,Component,Audio,Group,Relay;public byte FreeCA;}
  [StructLayout(LayoutKind.Sequential)]struct Short {public ushort NameLength;public IntPtr Name;public ushort TextLength;public IntPtr Text;}
  static T Bind<T>(IntPtr module,string name)where T:class {var p=GetProcAddress(module,name);if(p==IntPtr.Zero)throw new InvalidDataException("EDCB DLLにAPIがありません："+name);return Marshal.GetDelegateForFunctionPointer(p,typeof(T))as T;}
  public static void Fill(string file,string dll,Metadata m,CancellationToken token) {
   lock(gate) {
    IntPtr module=LoadLibraryEx(Path.GetFullPath(dll),IntPtr.Zero,0x100|0x1000);if(module==IntPtr.Zero)throw new InvalidDataException("EDCB x64 DLLを読み込めません（"+Marshal.GetLastWin32Error()+"）。");uint id=0;Uninit uninit=null;bool initialized=false;
    try {
     var init=Bind<Init>(module,"InitializeEP");uninit=Bind<Uninit>(module,"UnInitializeEP");var add=Bind<Add>(module,"AddTSPacketEP");var services=Bind<Services>(module,"GetServiceListActualEP");var current=Bind<Event>(module,"GetEpgInfoEP");
     if(init(0,out id)!=1)throw new InvalidDataException("EPG解析の初期化に失敗。");initialized=true;
     // Up to 64 MiB from the beginning; only PSI/SI packets are handed to the parser.
     using(var input=File.OpenRead(file)) {
      byte[] packet=new byte[188];long read=0;while(read<64*1024*1024&&input.Read(packet,0,188)==188) {
       read+=188;if(read%(188*1024)==0)token.ThrowIfCancellationRequested();
       if(packet[0]!=0x47){input.Position-=187;continue;}int pid=((packet[1]&31)<<8)|packet[2];if(pid<32)add(id,packet,188);
      }
     }
     uint count;IntPtr list;if(services(id,out count,out list)!=1||count>1024)return;
     for(int i=0;i<count;i++) {
      var service=(Service)Marshal.PtrToStructure(IntPtr.Add(list,i*Marshal.SizeOf(typeof(Service))),typeof(Service));if(m.ServiceId!=0&&service.Sid!=m.ServiceId)continue;
      m.NetworkId=service.Onid;m.TransportId=service.Tsid;m.ServiceId=service.Sid;
      if(service.Ext!=IntPtr.Zero){var ext=(ServiceExt)Marshal.PtrToStructure(service.Ext,typeof(ServiceExt));m.Channel=Marshal.PtrToStringUni(ext.Name)??"";}
      IntPtr ptr;if(current(id,service.Onid,service.Tsid,service.Sid,0,out ptr)==1&&ptr!=IntPtr.Zero) {
       var ev=(EventInfo)Marshal.PtrToStructure(ptr,typeof(EventInfo));m.EventId=ev.Id;
       if(ev.StartFlag!=0)try{m.Start=new DateTime(ev.Start.Year,ev.Start.Month,ev.Start.Day,ev.Start.Hour,ev.Start.Minute,ev.Start.Second);}catch(ArgumentOutOfRangeException){}
       if(ev.Short!=IntPtr.Zero){var s=(Short)Marshal.PtrToStructure(ev.Short,typeof(Short));m.Title=Marshal.PtrToStringUni(s.Name)??"";m.Description=Marshal.PtrToStringUni(s.Text)??"";}
       m.Source="TS内のEIT/SDT（EpgDataCap3）";
      }
      break;
     }
    }finally{if(initialized)uninit(id);FreeLibrary(module);}
   }
  }
 }
}
