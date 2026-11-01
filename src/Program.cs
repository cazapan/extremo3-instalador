using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ExtremeInstaller;

public sealed record Release(string Minecraft, string NeoForge, string NeoSha256, long FileId,
    string ModFile, string ModSha256, bool Reviewed, string ModUrl="", string JavaUrl="", string JavaSha256="",
    string MinecraftMetadataUrl="", string MinecraftMetadataSha1="");

public static class Core
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public const string Page = "https://www.curseforge.com/minecraft/mc-mods/extreme-3-mechanics/files/8401810";
    public static Release Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(x => x.EndsWith("release-lock.json")))!;
        return JsonSerializer.Deserialize<Release>(s, Json)!;
    }
    public static void Validate(Release r)
    {
        if (!r.Reviewed || !Regex.IsMatch(r.ModSha256, "^[a-fA-F0-9]{64}$"))
            throw new InvalidOperationException("Esta compilación está pendiente de preparación por el organizador. Debe descargar el JAR oficial, ejecutar tools/seal_release.py y recompilar. No se instalarán archivos sin un hash fijado.");
        if (r.Minecraft != "1.21.1" || !Regex.IsMatch(r.NeoForge, @"^21\.1\.\d+$") ||
            !Regex.IsMatch(r.NeoSha256, "^[a-fA-F0-9]{64}$") || r.ModFile != "mcextremo-v14.jar" || r.FileId != 8401810)
            throw new InvalidDataException("Manifiesto no compatible.");
    }
    public static void Verify(string path, string hash)
    {
        using var s = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(s)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El archivo no coincide con el SHA-256 fijado. No se instalará: " + Path.GetFileName(path));
    }
    public static void AtomicJson(string file, JsonNode value)
    {
        var temp = file + ".extremo-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, value.ToJsonString(Json)); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task Download(string url, string dest, string hash, CancellationToken ct)
    {
        // Only a pinned installer from the official Maven, never arbitrary URLs from a manifest.
        if (!url.StartsWith("https://maven.neoforged.net/releases/net/neoforged/neoforge/", StringComparison.Ordinal))
            throw new InvalidDataException("Origen no permitido.");
        var temp = dest + ".part";
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 100_000_000) throw new InvalidDataException("Descarga demasiado grande.");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(temp))
            {
                byte[] buffer = new byte[65536]; long total = 0; int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                { total += n; if (total > 100_000_000) throw new InvalidDataException("Descarga demasiado grande."); await output.WriteAsync(buffer.AsMemory(0, n), ct); }
            }
            Verify(temp, hash);
            using (var zip = ZipFile.OpenRead(temp))
            {
                using var reader = new StreamReader(zip.GetEntry("install_profile.json")!.Open());
                var p = JsonNode.Parse(await reader.ReadToEndAsync(ct))!;
                if ((string?)p["minecraft"] != "1.21.1") throw new InvalidDataException("NeoForge para otra versión.");
            }
            File.Move(temp, dest, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task<string> Run(string exe, IEnumerable<string> args, string cwd, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cwd };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new IOException("No se pudo iniciar Java.");
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        using var registration = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } });
        await p.WaitForExitAsync(ct);
        var text = await stdout + "\n" + await stderr;
        if (p.ExitCode != 0) throw new IOException("Java/NeoForge terminó con código " + p.ExitCode + ".\n" + text[^Math.Min(text.Length, 4000)..]);
        return text;
    }
    public static async Task CheckJava(string java, CancellationToken ct)
    {
        var s = await Run(java, new[] { "-XshowSettings:properties", "-version" }, Path.GetTempPath(), ct);
        if (!Regex.IsMatch(s, @"java\.version\s*=\s*21(?:\.|\s)") || !Regex.IsMatch(s, @"sun\.arch\.data\.model\s*=\s*64"))
            throw new InvalidOperationException("Seleccioná Java 21 de 64 bits (java.exe). El instalador no cambia el Java del sistema.");
    }
    public static void CheckNoLinks(string path)
    {
        for (DirectoryInfo? d = new(Path.GetFullPath(path)); d != null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("No se admiten rutas con enlaces o junctions.");
    }
    public static void WritePrism(string stage, Release r)
    {
        File.WriteAllText(Path.Combine(stage, "instance.cfg"), "[General]\nConfigVersion=1.2\nInstanceType=OneSix\nname=Extremo 3 - 1.21.1\n");
        AtomicJson(Path.Combine(stage, "mmc-pack.json"), new JsonObject { ["formatVersion"] = 1,
            ["components"] = new JsonArray(new JsonObject { ["uid"]="net.minecraft", ["version"]=r.Minecraft, ["important"]=true },
                new JsonObject { ["uid"]="net.neoforged", ["version"]=r.NeoForge, ["important"]=true }) });
    }
    public static List<(string Name, string Path)> Detect()
    {
        var app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[] { ("Oficial / carpeta Minecraft", Path.Combine(app,".minecraft")),
            ("Prism Launcher",Path.Combine(app,"PrismLauncher")), ("MultiMC",Path.Combine(app,"MultiMC")),
            ("ATLauncher",Path.Combine(app,"ATLauncher")), ("Modrinth App",Path.Combine(app,"com.modrinth.theseus")),
            ("CurseForge",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"curseforge","minecraft")),
            ("TLauncher",Path.Combine(app,".tlauncher")), ("SKlauncher",Path.Combine(app,".sklauncher")),
            ("Prism Launcher (local)",Path.Combine(local,"PrismLauncher")) };
        return roots.Where(x=>Directory.Exists(x.Item2)).Select(x=>(x.Item1,x.Item2)).ToList();
    }
}

public sealed class InstallerForm : Form
{
    readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 660 };
    readonly TextBox root = new() { Width=550 };
    readonly TextBox log = new() { Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Width=760, Height=190 };
    readonly Button install = new() { Text="Instalar", AutoSize=true };
    readonly ProgressBar progress = new() { Width=760, Height=8, Style=ProgressBarStyle.Marquee, Visible=false };
    readonly Button cancel = new() { Text="Cancelar", AutoSize=true, Enabled=false };
    readonly Release release = Core.Load();
    CancellationTokenSource? cts;
    bool busy;
    public InstallerForm()
    {
        Text="Instalar Extremo 3"; Width=820; Height=630; MinimumSize=new(820,630); AutoScaleMode=AutoScaleMode.Dpi;
        var panel = new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, Padding=new(18), AutoScroll=true };
        Controls.Add(panel);
        void Label(string text) => panel.Controls.Add(new Label { Text=text, AutoSize=true, MaximumSize=new(760,0), Margin=new(0,8,0,5) });
        Label("Minecraft 1.21.1 · NeoForge " + release.NeoForge + " · Extreme 3 Mechanics v14\nSin cuentas ni contraseñas. Cerrá Minecraft y los launchers antes de continuar.");
        Label(release.Reviewed ? "Pulsá Instalar. Descargaremos y verificaremos todo automáticamente." : "PREPARACIÓN: falta configurar la descarga del mod. Consultá LEEME.md.");
        Label("Detectados: " + string.Join("; ", Core.Detect().Select(x=>x.Name)) + "\nLa detección por carpetas es orientativa; para versiones portables elegí el modo manual.");
        mode.Items.AddRange(new[] { "Oficial: instalar NeoForge y crear perfil con carpeta aislada", "Prism: crear la instancia automáticamente", "Otros / portable: preparar carpeta aislada e instrucciones" }); mode.SelectedIndex=0; panel.Controls.Add(mode);
        var pathRow=Row(root, "Elegir…", ()=> { using var d=new FolderBrowserDialog(); if(d.ShowDialog()==DialogResult.OK) root.Text=d.SelectedPath; });
        pathRow.Visible=false;
        var advanced=new Button { Text="Cambiar carpeta…", AutoSize=true };
        advanced.Click+=(_,_)=>pathRow.Visible=!pathRow.Visible;
        panel.Controls.Add(advanced); panel.Controls.Add(pathRow);
        root.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),".minecraft");
        mode.SelectedIndexChanged += (_,_)=>root.Text=mode.SelectedIndex==0 ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),".minecraft") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Extremo3");
        var actions=new FlowLayoutPanel { Width=760, Height=40 }; actions.Controls.AddRange(new Control[]{install,cancel}); panel.Controls.Add(actions);
        panel.Controls.Add(progress); panel.Controls.Add(log);
        var detected=Core.Detect();
        var prism=detected.FirstOrDefault(x=>x.Name.StartsWith("Prism Launcher"));
        if(!string.IsNullOrEmpty(prism.Path)) { mode.SelectedIndex=1; root.Text=Path.Combine(prism.Path,"instances"); }
        mode.SelectedIndexChanged += (_,_)=> { if(mode.SelectedIndex==1 && !string.IsNullOrEmpty(prism.Path)) root.Text=Path.Combine(prism.Path,"instances"); };
        install.Click+=async(_,_)=>await Install(); cancel.Click+=(_,_)=>cts?.Cancel();
        FormClosing+=(_,e)=> { if(busy) { e.Cancel=true; cts?.Cancel(); Note("Cancelando; esperá a que termine la operación."); } };
    }
    static Control Row(Control field,string text,Action action) { var row=new FlowLayoutPanel{ Width=760,Height=34 }; var b=new Button{Text=text,AutoSize=true}; b.Click+=(_,_)=>action(); row.Controls.AddRange(new[]{field,b}); return row; }
    void Note(string s)=>log.AppendText(s+Environment.NewLine);
    async Task Install()
    {
        string? stage=null; bool complete=false;
        try
        {
            Automatic.Validate(release);
            var target=Path.GetFullPath(root.Text.Trim()); Core.CheckNoLinks(target);
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);
            using var gate=new FileStream(Path.Combine(target,".extremo-install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            cts=new CancellationTokenSource(TimeSpan.FromMinutes(30)); var ct=cts.Token; busy=true;
            install.Enabled=false; cancel.Enabled=true; mode.Enabled=false; root.Enabled=false; progress.Visible=true;
            var selected=mode.SelectedIndex;
            if(selected==0 && !File.Exists(Path.Combine(target,"launcher_profiles.json")))
                throw new InvalidOperationException("Abrí el launcher oficial una vez y cerralo para que cree su configuración, o elegí la carpeta correcta.");
            if((selected==0 && (Process.GetProcessesByName("MinecraftLauncher").Length>0 || Process.GetProcessesByName("javaw").Length>0)) ||
                (selected==1 && Process.GetProcessesByName("prismlauncher").Length>0))
                throw new InvalidOperationException("Cerrá Minecraft y el launcher antes de instalar.");
            stage=Path.Combine(target,".extremo-stage-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            var game=Path.Combine(stage,selected==1?".minecraft":"game"); Directory.CreateDirectory(Path.Combine(game,"mods"));
            Note("Descargando y verificando Extreme 3 Mechanics…");
            await Automatic.Fetch(release.ModUrl,Path.Combine(game,"mods",release.ModFile),release.ModSha256,ct);
            Note("Preparando Java 21 para esta instancia…");
            var javaPath=await Automatic.Java(stage,release,ct);
            var javaRelative=Path.GetRelativePath(stage,javaPath);
            var final=Path.Combine(target,"Extremo3-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
            if(selected==0)
            {
                var profiles=Path.Combine(target,"launcher_profiles.json");
                if(!File.Exists(profiles))
                    throw new InvalidOperationException("Abrí el launcher oficial una vez y cerralo, o elegí su carpeta correcta.");
                if(Process.GetProcessesByName("MinecraftLauncher").Length>0 || Process.GetProcessesByName("javaw").Length>0)
                    throw new InvalidOperationException("Cerrá Minecraft y el launcher antes de instalar.");
                Note("Comprobando Java 21 de 64 bits…"); await Core.CheckJava(javaPath,ct);
                var installer=Path.Combine(stage,"neoforge-installer.jar");
                Note("Descargando NeoForge del Maven oficial y verificando SHA-256…");
                await Core.Download($"https://maven.neoforged.net/releases/net/neoforged/neoforge/{release.NeoForge}/neoforge-{release.NeoForge}-installer.jar",installer,release.NeoSha256,ct);
                // Work in a private launcher root: official installer cannot edit existing user profiles.
                var neoRoot=Path.Combine(stage,"loader"); Directory.CreateDirectory(neoRoot);
                File.WriteAllText(Path.Combine(neoRoot,"launcher_profiles.json"),"{\"profiles\":{}}");
                Note("Descargando Minecraft 1.21.1 desde Mojang y verificando sus hashes…");
                await Automatic.Vanilla(neoRoot,release,ct);
                Note("Instalando NeoForge en un área privada; puede tardar varios minutos…");
                var output=await Core.Run(javaPath,new[]{"-jar",installer,"--installClient",neoRoot},stage,ct);
                File.WriteAllText(Path.Combine(stage,"neoforge-install.log"),output);
                var version="neoforge-"+release.NeoForge;
                var versionJson=Path.Combine(neoRoot,"versions",version,version+".json");
                var v=JsonNode.Parse(File.ReadAllText(versionJson))!;
                if((string?)v["id"]!=version || (string?)v["inheritsFrom"]!="1.21.1") throw new InvalidDataException("Perfil NeoForge inesperado.");
                ct.ThrowIfCancellationRequested();
                // Never overwrite shared libraries or existing version definitions with different bytes.
                Merge(Path.Combine(neoRoot,"versions","1.21.1"),Path.Combine(target,"versions","1.21.1"));
                Merge(Path.Combine(neoRoot,"libraries"),Path.Combine(target,"libraries"));
                Merge(Path.Combine(neoRoot,"versions",version),Path.Combine(target,"versions",version));
                if(!Path.GetFullPath(neoRoot).StartsWith(Path.GetFullPath(stage)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Carpeta temporal inesperada.");
                Core.CheckNoLinks(neoRoot); Directory.Delete(neoRoot,true); File.Delete(installer);
                var before=File.ReadAllBytes(profiles);
                var doc=JsonNode.Parse(before)!; var entries=doc["profiles"] as JsonObject ?? throw new InvalidDataException("Perfiles inválidos.");
                var key="extremo3-"+Guid.NewGuid().ToString("N");
                entries[key]=new JsonObject { ["name"]="Extremo 3 · 1.21.1", ["type"]="custom", ["lastVersionId"]=version,
                    ["gameDir"]=Path.Combine(final,"game"), ["javaDir"]=Path.Combine(final,javaRelative) };
                File.WriteAllBytes(profiles+".extremo-backup-"+Guid.NewGuid().ToString("N")+".json",before);
                if(!File.ReadAllBytes(profiles).SequenceEqual(before)) throw new IOException("El launcher cambió los perfiles; cerralo y reintentá.");
                Directory.Move(stage,final); stage=null;
                Core.AtomicJson(profiles,doc);
                Note("Listo. Abrí el launcher oficial y elegí «Extremo 3 · 1.21.1».\nInstancia: "+Path.Combine(final,"game"));
            }
            else if(selected==1)
            {
                if(Process.GetProcessesByName("prismlauncher").Length>0)
                    throw new InvalidOperationException("Cerrá Prism y volvé a pulsar Instalar.");
                Core.WritePrism(stage,release);
                File.AppendAllText(Path.Combine(stage,"instance.cfg"),"OverrideJavaLocation=true\nJavaPath="+Path.Combine(final,javaRelative).Replace('\\','/')+"\n");
                ct.ThrowIfCancellationRequested(); Directory.Move(stage,final); stage=null;
                Note("Listo. Abrí Prism y elegí «Extremo 3 - 1.21.1». En el primer inicio Prism descargará Minecraft y NeoForge automáticamente.");
            }
            else
            {
                Note("Preparando NeoForge verificado para la configuración manual…");
                await Core.Download($"https://maven.neoforged.net/releases/net/neoforged/neoforge/{release.NeoForge}/neoforge-{release.NeoForge}-installer.jar",Path.Combine(stage,"neoforge-installer.jar"),release.NeoSha256,ct);
                File.WriteAllText(Path.Combine(stage,"PASOS.txt"),$"Creá una instancia NUEVA en tu launcher: Minecraft 1.21.1, NeoForge {release.NeoForge}, Java 21 x64.\r\nIndicá como carpeta del juego: {Path.Combine(final,"game")}\r\nSi no permite cambiar la carpeta del juego o instalar NeoForge exacto, usá Prism. No apuntes a una instancia existente.\r\nEl JAR de NeoForge adjunto es su instalador oficial verificado; no lo pongas en mods.\r\nEl mod ya está en game/mods. No se han modificado perfiles de tu launcher.\r\nIniciá sesión únicamente desde tu launcher habitual.\r\n");
                ct.ThrowIfCancellationRequested(); Directory.Move(stage,final); stage=null;
                Note("Carpeta preparada: "+final+"\nLeé PASOS.txt. Falta crear la instancia en tu launcher; este modo no afirma haberla instalado.");
            }
            complete=true;
        }
        catch(OperationCanceledException) { Note("Operación cancelada o tiempo agotado. Podés reintentar."); }
        catch(Exception ex) { Note("No se completó: "+ex.Message); }
        finally
        {
            if(stage!=null && Directory.Exists(stage)) { try { Directory.Delete(stage,true); } catch(IOException) { Note("Archivos temporales conservados en: "+stage); } }
            busy=false; install.Enabled=true; cancel.Enabled=false; mode.Enabled=true; root.Enabled=true; progress.Visible=false;
            cts?.Dispose(); cts=null;
            if(complete) Note("No se solicitaron credenciales. Conservá tus mundos y usá la misma versión que el servidor.");
        }
    }
    static void Merge(string source,string dest)
    {
        Core.CheckNoLinks(dest);
        foreach(var f in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
        {
            var d=Path.Combine(dest,Path.GetRelativePath(source,f)); Core.CheckNoLinks(Path.GetDirectoryName(d)!);
            Directory.CreateDirectory(Path.GetDirectoryName(d)!);
            if(File.Exists(d)) { using var s=File.OpenRead(f); Core.Verify(d,Convert.ToHexString(SHA256.HashData(s))); }
            else { var tmp=d+".extremo-"+Guid.NewGuid().ToString("N"); File.Copy(f,tmp); File.Move(tmp,d); }
        }
    }
}
internal static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new InstallerForm()); }
}
