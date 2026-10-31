using System;
using System.Collections.Generic;
using System.Linq;

namespace TsEncodeFlow {
 public static class PresetCatalog {
  public static string EngineName(Engine engine)=>engine==Engine.CPU?"CPU":engine==Engine.NVEncC?"NVIDIA":engine==Engine.VCEEncC?"AMD":"Intel QSV";
  public static string ResolutionName(Preset p)=>p.Width==0&&p.Height==0?"解像度を維持":p.Width+" × "+p.Height;
  public static string FilterName(Deinterlace d)=>d==Deinterlace.None?"解除なし":d==Deinterlace.Yadif?"Yadif・標準":d==Deinterlace.YadifBob?"Yadif・なめらか":d==Deinterlace.Bwdif?"Bwdif・標準":"Bwdif・なめらか";
  public static List<Preset> Create() {
   var result=new List<Preset>();
   foreach(Engine engine in Enum.GetValues(typeof(Engine))) {
    Add("general-balance","通常番組","毎日の番組・バランス","画質とファイルサイズのバランスを重視。迷ったらこれ。",Codec.H264,23,1280,720,Deinterlace.Yadif);
    Add("general-quality","通常番組","毎日の番組・高画質","解像度を保ち、動きのある映像もなめらかに。",Codec.H264,20,0,0,Deinterlace.YadifBob);
    Add("general-small","通常番組","毎日の番組・省容量","HEVCと720pで、録画をコンパクトに残す。",Codec.HEVC,26,1280,720,Deinterlace.Yadif);
    Add("anime-quality","アニメ","アニメ・輪郭を大切に","弱い圧縮で線と色を残す。過剰なノイズ除去は行いません。",Codec.H264,19,0,0,Deinterlace.Bwdif);
    Add("anime-small","アニメ","アニメ・コレクション","HEVC・720pで、シリーズをまとめて保存。",Codec.HEVC,23,1280,720,Deinterlace.Bwdif);
    Add("anime-24","アニメ","アニメ・24p素材向け","24p素材に合わせて23.976fpsへ。混在素材には標準プリセットを。",Codec.HEVC,21,1920,1080,Deinterlace.Bwdif,"24000/1001");
    Add("movie-quality","映画","映画・ディテール重視","HEVCで階調や細部を残す。サイズより画質を優先。",Codec.HEVC,19,0,0,Deinterlace.Bwdif);
    Add("movie-compatible","映画","映画・再生互換性重視","H.264・MP4で、幅広い再生環境へ。",Codec.H264,20,1920,1080,Deinterlace.Yadif);
    Add("sports-smooth","スポーツ","スポーツ・なめらか","Yadifの倍フレーム出力で、速い動きも見やすく。",Codec.H264,21,1280,720,Deinterlace.YadifBob);
    Add("mobile-small","モバイル","持ち歩き・小さく保存","360p・AAC 128kbpsで、移動中の視聴用に。",Codec.H264,25,640,360,Deinterlace.Yadif);
    Add("archive-mkv","通常番組","音声を残す・MKV","元の音声をコピー。映像はH.264に変換します。",Codec.H264,21,0,0,Deinterlace.Yadif,"",Container.mkv,true);
    Add("av1-modern","通常番組","AV1・新しい再生環境向け","AV1対応GPU・エンコーダー・再生機器が必要です。",Codec.AV1,28,1280,720,Deinterlace.Yadif);
    void Add(string id,string category,string name,string summary,Codec codec,int quality,int w,int h,Deinterlace deinterlace,string fps="",Container container=Container.mp4,bool audioCopy=false) {
     result.Add(new Preset {FactoryId=id+"-"+engine,Name=name,Category=category,Summary=summary,Engine=engine,Codec=codec,Quality=quality,Width=w,Height=h,Deinterlace=deinterlace,Fps=fps,Container=container,CopyAudio=audioCopy,AudioBitrate=category=="モバイル"?128:192,Speed=engine==Engine.CPU?(codec==Codec.AV1?"8":"medium"):engine==Engine.NVEncC?"P4":"balanced",ExtraArgs=engine==Engine.CPU&&id=="anime-quality"?"-tune animation":"",Notes=fps.Length>0?"フレームの間引き・複製を行います。逆テレシネの自動判定ではありません。GPU利用時は可逆中間ファイルに大きな空き容量が必要です。":codec==Codec.AV1?"Radeon RX 570など、AV1エンコード非対応のGPUでは利用できません。":"素材により最適な設定は異なります。短いTSで画質を確認してください。"});
    }
   }
   return result;
  }
  public static void Upgrade(Settings s) {
   var catalog=Create();var missing=catalog.Where(p=>!s.Presets.Any(x=>x.FactoryId==p.FactoryId)).ToList();s.Presets.AddRange(missing);
  }
 }
}
