using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ExtremeInstaller;

public static class Repair
{
    public static string? Windowless(string executable,string instance)
    {
        if(!Path.IsPathFullyQualified(executable) || Path.GetFileName(executable)!="java.exe") return null;
        var prefix=Path.GetFullPath(instance)+Path.DirectorySeparatorChar;
        var runtime=Path.GetFullPath(Path.Combine(instance,"runtime"))+Path.DirectorySeparatorChar;
        var candidate=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!,"javaw.exe"));
        if(!candidate.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) || !candidate.StartsWith(runtime,StringComparison.OrdinalIgnoreCase)) return null;
        Core.CheckNoLinks(Path.GetDirectoryName(candidate)!);
        return File.Exists(candidate)?candidate:null;
    }

    public static int Official(string root)
    {
        var file=Path.Combine(root,"launcher_profiles.json");
        if(!File.Exists(file)) return 0;
        Core.CheckNoLinks(root);
        var before=File.ReadAllBytes(file);
        var doc=JsonNode.Parse(before)!;
        if(doc["profiles"] is not JsonObject profiles) return 0;
        int count=0;
        foreach(var p in profiles)
        {
            if(!p.Key.StartsWith("extremo3-",StringComparison.Ordinal) || p.Value is not JsonObject profile) continue;
            var game=(string?)profile["gameDir"]; var executable=(string?)profile["javaDir"];
            if(game==null || executable==null || !Path.IsPathFullyQualified(game)) continue;
            var instance=Directory.GetParent(game)?.FullName;
            if(instance==null || !Path.GetFileName(instance).StartsWith("Extremo3-",StringComparison.Ordinal) || Path.GetFileName(game)!="game") continue;
            var candidate=Windowless(executable,instance);
            if(candidate==null) continue;
            profile["javaDir"]=candidate;count++;
        }
        if(count>0)
        {
            File.WriteAllBytes(file+".extremo-console-backup-"+Guid.NewGuid().ToString("N")+".json",before);
            if(!File.ReadAllBytes(file).SequenceEqual(before)) throw new IOException("El launcher cambió sus perfiles. Cerralo y reintentá.");
            Core.AtomicJson(file,doc);
        }
        return count;
    }

    public static int Prism(string root)
    {
        if(!Directory.Exists(root)) return 0;
        int count=0;
        foreach(var dir in Directory.GetDirectories(root,"Extremo3-*",SearchOption.TopDirectoryOnly))
        {
            Core.CheckNoLinks(dir);
            var cfg=Path.Combine(dir,"instance.cfg");
            if(!File.Exists(cfg)) continue;
            var before=File.ReadAllText(cfg);
            var match=Regex.Match(before,@"(?m)^JavaPath=([^\r\n]+)");
            if(!match.Success) continue;
            var candidate=Windowless(match.Groups[1].Value.Replace('/',Path.DirectorySeparatorChar),dir);
            if(candidate==null) continue;
            File.Copy(cfg,cfg+".console-backup-"+Guid.NewGuid().ToString("N"));
            if(File.ReadAllText(cfg)!=before) throw new IOException("Prism cambió la configuración. Cerralo y reintentá.");
            var text=before[..match.Index]+"JavaPath="+candidate.Replace('\\','/')+before[(match.Index+match.Length)..];
            var tmp=cfg+".tmp-"+Guid.NewGuid().ToString("N");
            File.WriteAllText(tmp,text);File.Move(tmp,cfg,true);count++;
        }
        return count;
    }
}
