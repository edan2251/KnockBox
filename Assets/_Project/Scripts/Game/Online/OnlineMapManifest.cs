using System;
using Knockbox.Simulation;

namespace Knockbox.Game.Online
{
    /// <summary>프로토콜 9의 경기 맵 목록 검증. 리플레이 v8의 과거 체크섬은 변경하지 않는다.</summary>
    public static class OnlineMapManifest
    {
        public static uint Compute(ArenaDef[] arenas)
        {
            if(arenas==null||arenas.Length==0||arenas.Length>ArenaRotation.MaxArenas)
                throw new ArgumentException("온라인 경기 맵은 1~"+ArenaRotation.MaxArenas+"개여야 합니다.",nameof(arenas));
            var h=Hasher.Create();
            h.AddUInt(0x394D424B); // KBM9: do not alias the historical v8 map checksum.
            h.AddInt(arenas.Length);
            foreach(var arena in arenas)
            {
                if(arena==null)throw new ArgumentException("온라인 경기 맵이 비어 있습니다.",nameof(arenas));
                string name=arena.Name??"";
                h.AddInt(name.Length);
                foreach(char c in name)h.AddInt(c);
                h.AddUInt(arena.ComputeGeometryChecksum());
            }
            return h.Hash;
        }
    }
}
