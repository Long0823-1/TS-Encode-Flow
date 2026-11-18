using System;
using System.IO;

namespace TsEncodeFlow {
 // 入力の判定は拡張子ではなくファイルの中身で行う（Issue #5）。
 // MPEG-TS は同期バイト 0x47 がパケット周期で並ぶことで識別する。
 // パケット長は通常のTSの188バイトと、.m2ts（BDAV）で使われる
 // 4バイトの TP_extra ヘッダ付き192バイトの2形式に対応する。
 // 下流の ffmpeg / ffprobe / L-SMASH はどちらの形式もそのまま読めるため、
 // 変換パイプライン側の入力は変えず、自前でパケットを走査する箇所
 // （TS診断・EPG取得・字幕解析）だけがパケット長を意識する。
 public static class TransportStream {
  public const int CorePacketSize=188;
  // 先頭バイト列からパケット長を判定する。TSと確認できなければ0。
  public static int DetectPacketSize(ReadOnlySpan<byte> head) {
   if(head.Length>=3*188&&head[0]==0x47&&head[188]==0x47&&head[376]==0x47)return 188;
   if(head.Length>=4+3*192&&head[4]==0x47&&head[4+192]==0x47&&head[4+2*192]==0x47)return 192;
   return 0;
  }
  // ファイル先頭を読んでパケット長を返す。読めない・確認できない場合は0。
  public static int PacketSize(string file) {
   try {
    using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    byte[] head=new byte[4+3*192];int read=0;
    while(read<head.Length){int n=stream.Read(head,read,head.Length-read);if(n==0)break;read+=n;}
    return DetectPacketSize(new ReadOnlySpan<byte>(head,0,read));
   }catch(IOException){return 0;}catch(UnauthorizedAccessException){return 0;}
  }
  // 192バイト形式で実パケット（188バイト）が始まるオフセット。
  public static int HeaderOffset(int packetSize)=>packetSize-CorePacketSize;
  // 「TSではない」と断定できるか。判定には最低3パケット分の長さが要るため、
  // それより短いファイル（録画開始直後・0バイト等）は断定せず、従来どおり
  // 後段の安定待ち・0バイト判定・ffprobe の結果に委ねる。
  public static bool IsDefinitelyNot(string file) {
   try {
    var info=new FileInfo(file);
    if(!info.Exists||info.Length<4+3*192)return false;
    return PacketSize(file)==0;
   }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
  }
  // キュー登録時の入力チェック。存在しない場合と、TSでないことが中身から
  // 断定できる場合だけを弾く。拡張子は一切見ない。
  public static void RequireRecognized(string path) {
   if(!File.Exists(path))throw new ArgumentException("存在するTSファイルを指定してください："+path);
   if(IsDefinitelyNot(path))throw new ArgumentException("ファイルの中身がMPEG-TSではありません（拡張子ではなく中身で判定しています）："+path);
  }
 }
}
