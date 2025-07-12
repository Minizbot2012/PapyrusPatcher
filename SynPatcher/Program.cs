using Mutagen.Bethesda;
using Mutagen.Bethesda.Pex;
using Mutagen.Bethesda.Json;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using System.Data;
using Newtonsoft.Json;
using Noggog;

namespace SynPatcher;
internal class Program
{
    public static JsonSerializerSettings settings = new();
    static async Task<int> Main(string[] args)
    {
        return await SynthesisPipeline.Instance
            .AddPatch<ISkyrimMod, ISkyrimModGetter>(Patch)
            .SetTypicalOpen(GameRelease.SkyrimSE, "PEXPATCH.esp")
            .Run(args);
    }

    static void Patch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        var patched = new HashSet<string>();
        settings.AddMutagenConverters();
        settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        settings.NullValueHandling = NullValueHandling.Ignore;
        settings.MissingMemberHandling = MissingMemberHandling.Error;
        string patch_dir = Path.Join(state.DataFolderPath, "SynPKGs", "PapyrusPatcher");
        foreach (var pchFile in Directory.GetFiles(patch_dir))
        {
            Console.WriteLine($"Running {pchFile}");
            if (!pchFile.EndsWith(".json")) continue;
            var conf = JsonConvert.DeserializeObject<Config>(File.ReadAllText($"{pchFile}"), settings);
            if (conf.pxnName == string.Empty) continue;
            foreach (var file in conf.patches)
            {
                Console.WriteLine($"Patching {file.FileName}");
                var scriptName = Path.Join("Scripts", file.FileName);
                var scriptBAKName = Path.Join(state.DataFolderPath, "Scripts", "BAK", file.FileName);
                if (state.AssetProvider.Exists(scriptName) && state.AssetProvider.TryGetStream(scriptName, out Stream? strm))
                {
                    if (strm != null)
                    {
                        PexFile pexed = PexFile.CreateFromStream(strm, GameCategory.Skyrim);
                        var pxns = pexed.MachineName.Split("-").ToHashSet();
                        if (!pxns.Contains("PXP") && !patched.Contains(file.FileName))
                        {
                            Console.WriteLine($"{file.FileName} is fresh, backing up and using.");
                            Directory.CreateDirectory(Path.Join(state.DataFolderPath, "Scripts", "BAK"));
                            pexed.WritePexFile(scriptBAKName, GameCategory.Skyrim);
                            pxns = ["PXP"];
                            patched.Add(file.FileName);
                        }
                        else if (!patched.Contains(file.FileName) && File.Exists(scriptBAKName))
                        {
                            Console.WriteLine($"{file.FileName} has not been patched this run and backup exists, using backup.");
                            var tmp = PexFile.CreateFromFile(scriptBAKName, GameCategory.Skyrim);
                            pxns = [.. tmp.MachineName.Split("-")];
                            if (pxns.Contains("PXP"))
                            {
                                Console.WriteLine("Backup tainted, skipping file");
                                continue;
                            }
                            else
                            {
                                pxns = ["PXP"];
                            }
                            patched.Add(file.FileName);
                            pexed = tmp;
                        }
                        else
                        {
                            Console.WriteLine($"{file.FileName} has been patched this run and marker found, using current file.");
                        }
                        if (pxns.Contains(conf.pxnName))
                        {
                            Console.WriteLine($"Papyrus file patched already, skipping {file.FileName}");
                            continue;
                        }
                        pxns.Add(conf.pxnName);
                        pexed.MachineName = string.Join("-", pxns);
                        foreach (var stat in file.states)
                        {
                            var obj = pexed.Objects.Where(x => x.Name?.Equals(stat.Obj, StringComparison.InvariantCultureIgnoreCase) ?? false).First();
                            var st = obj.States.Where(x => x.Name?.Contains(stat.State, StringComparison.InvariantCultureIgnoreCase) ?? false).First();
                            foreach (var patch in stat.funcPatch)
                            {
                                var fn = st.Functions.Where(x => x.FunctionName == patch.FunctionName).First().Function;
                                if (patch.NewLocals != null)
                                {
                                    fn.Locals.AddRange(patch.NewLocals);
                                }
                                if (patch.RewriteCode != null)
                                {
                                    var offset = 0;
                                    foreach (var rewrites in patch.RewriteCode)
                                    {
                                        var mth = rewrites.replace.GetMatches(fn.Instructions);
                                        foreach (var itm in mth)
                                        {
                                            fn.Instructions.RemoveRange(offset + itm.Item1, itm.Item2);
                                            fn.Instructions.InsertRange(offset + itm.Item1, rewrites.with);
                                            offset += rewrites.with.Count() - itm.Item2;
                                            Console.WriteLine($"OFFSET: {offset}, OC: {itm.Item1}, {itm.Item2}, NC ");
                                        }
                                    }
                                }
                                if (patch.InsertInstructions != null)
                                {
                                    foreach (var ins in patch.InsertInstructions)
                                    {
                                        var pre = fn.Instructions.FindAll(ins.pred.IsInst);
                                        foreach (var loc in pre)
                                        {
                                            var idx = fn.Instructions.IndexOf(loc);
                                            var sdc = ins.pred.GetMatched(fn.Instructions[idx]);
                                            var newInst = ins.instructions.Select(x => x.Transform(sdc));
                                            fn.Instructions.InsertRange(idx, newInst);
                                        }
                                    }
                                }
                                if (patch.RewriteInstruction != null)
                                {
                                    foreach (var rw in patch.RewriteInstruction)
                                    {
                                        var pre = fn.Instructions.FindAll(rw.pred.IsInst);
                                        foreach (var inst in pre)
                                        {
                                            var sdc = rw.pred.GetMatched(inst);
                                            var rwi = fn.Instructions.IndexOf(inst);
                                            fn.Instructions[rwi] = rw.newInst.Transform(sdc);
                                        }
                                    }
                                }
                                if (patch.RewriteArgs != null)
                                {
                                    foreach (var rw in patch.RewriteArgs)
                                    {
                                        var pre = fn.Instructions.FindAll(rw.pred.IsInst);
                                        foreach (var loc in pre)
                                        {
                                            var rc = fn.Instructions.IndexOf(loc);
                                            var instruction = fn.Instructions[rc];
                                            foreach (var arg in rw.Arguments)
                                            {
                                                instruction.Arguments[arg.index] = arg.data;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if (pexed.DebugInfo != null)
                        {
                            pexed.DebugInfo.Functions.Clear();
                            pexed.DebugInfo.PropertyGroups.Clear();
                            pexed.DebugInfo.StructOrders.Clear();
                            pexed.DebugInfo.ModificationTime = DateTime.Now;
                        }
                        pexed.CompilationTime = DateTime.Now;
                        pexed.WritePexFile(Path.Join(state.DataFolderPath, scriptName), GameCategory.Skyrim);
                    }
                }
            }
        }
    }
}