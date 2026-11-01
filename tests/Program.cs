using ExtremeInstaller;
using System.Text.Json.Nodes;

var dir=Path.Combine(Path.GetTempPath(),"extremo-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
int count=0;
void Assert(bool value,string message) { if(!value) throw new Exception(message); count++; }
try
{
    var r=new Release("1.21.1","21.1.265",new string('a',64),8401810,"mcextremo-v14.jar",new string('b',64),true);
    Core.Validate(r);
    bool refused=false; try { Core.Validate(r with { Reviewed=false }); } catch(InvalidOperationException) { refused=true; }
    Assert(refused,"Unreviewed release accepted");
    refused=false; try { Core.Validate(r with { NeoForge="21.2.1" }); } catch(InvalidDataException) { refused=true; }
    Assert(refused,"Wrong Minecraft branch accepted");
    var f=Path.Combine(dir,"file.jar"); File.WriteAllText(f,"abc");
    Core.Verify(f,"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"); count++;
    refused=false; try { Core.Verify(f,new string('0',64)); } catch(InvalidDataException) { refused=true; }
    Assert(refused,"Tampered artifact accepted");
    var profiles=Path.Combine(dir,"profiles.json");
    Core.AtomicJson(profiles,JsonNode.Parse("{\"profiles\":{\"existing\":{\"name\":\"Keep me\"}}}")!);
    var json=JsonNode.Parse(File.ReadAllText(profiles))!;
    ((JsonObject)json["profiles"]!)["extremo"]=new JsonObject { ["gameDir"]="C:/isolated" };
    Core.AtomicJson(profiles,json);
    Assert((string?)JsonNode.Parse(File.ReadAllText(profiles))!["profiles"]!["existing"]!["name"]=="Keep me","Existing profile lost");
    Core.WritePrism(dir,r);
    var pack=JsonNode.Parse(File.ReadAllText(Path.Combine(dir,"mmc-pack.json")))!;
    Assert((string?)pack["components"]![1]!["uid"]=="net.neoforged","Wrong Prism loader UID");
    Assert(Automatic.ReleaseUrl("https://github.com/amigo/mod/releases/download/v14/mcextremo-v14.jar"),"Valid release URL rejected");
    Assert(!Automatic.ReleaseUrl("https://github.com/amigo/mod/releases/latest/download/mod.jar"),"Floating release accepted");
    Assert(!Automatic.ReleaseUrl("http://github.com/amigo/mod/releases/download/v14/mod.jar"),"HTTP accepted");
    Assert(!Automatic.Allowed(new Uri("https://github.com.evil.example/mod.jar")),"Untrusted host accepted");
    var unsafeZip=Path.Combine(dir,"unsafe.zip");
    using(var z=System.IO.Compression.ZipFile.Open(unsafeZip,System.IO.Compression.ZipArchiveMode.Create))
    { using var writer=new StreamWriter(z.CreateEntry("../escape.txt").Open()); writer.Write("no"); }
    refused=false; try { Automatic.ExtractRuntime(unsafeZip,Path.Combine(dir,"runtime")); } catch(InvalidDataException) { refused=true; }
    Assert(refused && !File.Exists(Path.Combine(dir,"escape.txt")),"ZIP traversal accepted");
    if(args.Contains("--network"))
    {
        var locked=Core.Load();
        var java=await Automatic.Java(dir,locked,CancellationToken.None);
        Assert(File.Exists(java),"Java not extracted");
        var loader=Path.Combine(dir,"loader");
        await Automatic.Vanilla(loader,locked,CancellationToken.None);
        Assert(File.Exists(Path.Combine(loader,"versions","1.21.1","1.21.1.jar")),"Vanilla not downloaded");
        if(args.Contains("--neo"))
        {
            Console.WriteLine("Installing official NeoForge into private test root…");
            File.WriteAllText(Path.Combine(loader,"launcher_profiles.json"),"{\"profiles\":{}}");
            var installer=Path.Combine(dir,"neo.jar");
            await Core.Download($"https://maven.neoforged.net/releases/net/neoforged/neoforge/{locked.NeoForge}/neoforge-{locked.NeoForge}-installer.jar",installer,locked.NeoSha256,CancellationToken.None);
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await Core.Run(java,new[]{"-jar",installer,"--installClient",loader},dir,timeout.Token);
            Assert(File.Exists(Path.Combine(loader,"versions","neoforge-"+locked.NeoForge,"neoforge-"+locked.NeoForge+".json")),"NeoForge profile not generated");
        }
    }
    Console.WriteLine($"PASS: {count} integrity/profile checks");
}
finally { Directory.Delete(dir,true); }
