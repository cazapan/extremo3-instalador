using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ExtremeInstaller;

public static class Automatic
{
    public static bool ReleaseUrl(string url) => Uri.TryCreate(url,UriKind.Absolute,out var u) &&
        u.Scheme=="https" && u.Host=="github.com" && u.IsDefaultPort && string.IsNullOrEmpty(u.UserInfo) &&
        Regex.IsMatch(u.AbsolutePath,@"^/[^/]+/[^/]+/releases/download/[^/]+/[^/]+$");

    public static void Validate(Release r)
    {
        Core.Validate(r);
        if(!ReleaseUrl(r.ModUrl) || !ReleaseUrl(r.JavaUrl) || !r.JavaUrl.StartsWith("https://github.com/adoptium/temurin21-binaries/releases/download/",StringComparison.Ordinal) ||
            !Regex.IsMatch(r.JavaSha256,"^[a-fA-F0-9]{64}$") || !Regex.IsMatch(r.MinecraftMetadataSha1,"^[a-fA-F0-9]{40}$") ||
            !r.MinecraftMetadataUrl.StartsWith("https://piston-meta.mojang.com/v1/packages/",StringComparison.Ordinal))
            throw new InvalidDataException("Falta configurar la descarga del mod en GitHub Releases. El organizador debe preparar esta versión antes de compartirla.");
    }

    public static bool Allowed(Uri u) => u.Scheme=="https" && u.IsDefaultPort && string.IsNullOrEmpty(u.UserInfo) &&
        new[]{"github.com","release-assets.githubusercontent.com","objects.githubusercontent.com","piston-meta.mojang.com","piston-data.mojang.com"}.Contains(u.Host);

    public static async Task Fetch(string url,string dest,string hash,CancellationToken ct)
    {
        if(!(Regex.IsMatch(hash,"^[a-fA-F0-9]{64}$") || Regex.IsMatch(hash,"^[a-fA-F0-9]{40}$"))) throw new InvalidDataException("Hash requerido.");
        var temp=dest+".part";
        try
        {
            using var handler=new HttpClientHandler { AllowAutoRedirect=false };
            using var client=new HttpClient(handler) { Timeout=TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ExtremeInstaller/2.0");
            var uri=new Uri(url);
            for(int redirects=0;redirects<=5;redirects++)
            {
                if(!Allowed(uri)) throw new InvalidDataException("Servidor de descarga no autorizado.");
                using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
                if((int)response.StatusCode>=300 && (int)response.StatusCode<400)
                {
                    var location=response.Headers.Location ?? throw new IOException("Redirección sin destino.");
                    uri=location.IsAbsoluteUri?location:new Uri(uri,location); continue;
                }
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>200_000_000) throw new IOException("Archivo demasiado grande.");
                await using(var input=await response.Content.ReadAsStreamAsync(ct))
                await using(var output=File.Create(temp))
                {
                    var buffer=new byte[65536]; int n; long total=0;
                    while((n=await input.ReadAsync(buffer,ct))>0)
                    { total+=n; if(total>200_000_000) throw new IOException("Archivo demasiado grande."); await output.WriteAsync(buffer.AsMemory(0,n),ct); }
                }
                using(var s=File.OpenRead(temp))
                {
                    var actual=hash.Length==64?SHA256.HashData(s):SHA1.HashData(s);
                    if(!Convert.ToHexString(actual).Equals(hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La descarga no coincide con el hash fijado: "+Path.GetFileName(dest));
                }
                File.Move(temp,dest,true); return;
            }
            throw new IOException("Demasiadas redirecciones.");
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }

    public static void ExtractRuntime(string zip,string dest)
    {
        Directory.CreateDirectory(dest);
        var prefix=Path.GetFullPath(dest)+Path.DirectorySeparatorChar;
        using var z=ZipFile.OpenRead(zip); long total=0;
        foreach(var e in z.Entries)
        {
            total+=e.Length;
            if(total>600_000_000 || ((e.ExternalAttributes>>16)&0xF000)==0xA000) throw new InvalidDataException("ZIP de Java inesperado.");
            var path=Path.GetFullPath(Path.Combine(dest,e.FullName.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ruta fuera de la carpeta Java.");
            if(e.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); e.ExtractToFile(path,false);
        }
    }

    public static async Task<string> Java(string stage,Release r,CancellationToken ct)
    {
        var zip=Path.Combine(stage,"java.zip");
        await Fetch(r.JavaUrl,zip,r.JavaSha256,ct);
        var runtime=Path.Combine(stage,"runtime");
        await Task.Run(()=>ExtractRuntime(zip,runtime),ct);
        var java=Directory.GetFiles(runtime,"java.exe",SearchOption.AllDirectories).Single();
        await Core.CheckJava(java,ct); File.Delete(zip); return java;
    }

    public static async Task Vanilla(string loader,Release r,CancellationToken ct)
    {
        var dest=Path.Combine(loader,"versions","1.21.1"); Directory.CreateDirectory(dest);
        var meta=Path.Combine(dest,"1.21.1.json");
        await Fetch(r.MinecraftMetadataUrl,meta,r.MinecraftMetadataSha1,ct);
        var json=JsonNode.Parse(await File.ReadAllTextAsync(meta,ct))!;
        if((string?)json["id"]!="1.21.1") throw new InvalidDataException("Minecraft incorrecto.");
        var client=json["downloads"]!["client"]!;
        var url=(string)client["url"]!;
        if(new Uri(url).Host!="piston-data.mojang.com") throw new InvalidDataException("Origen de Minecraft inesperado.");
        await Fetch(url,Path.Combine(dest,"1.21.1.jar"),(string)client["sha1"]!,ct);
    }
}
