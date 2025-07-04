﻿using Mutagen.Bethesda;
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
    public static HashSet<string> ESDPs = [];
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
        if (!Directory.Exists(state.ExtraSettingsDataPath))
        {
            Directory.CreateDirectory(state.ExtraSettingsDataPath ?? "Data/SynPEXPatcher/");
        }
        ESDPs.Add("Data/SynPEXPatcher/");
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
                var scriptName = Path.Join(state.DataFolderPath, "Scripts", file.FileName);
                var scriptBAKName = Path.Join(state.DataFolderPath, "Scripts", "BAK", file.FileName);
                if (File.Exists(scriptName))
                {
                    PexFile pexed = PexFile.CreateFromFile(scriptName, GameCategory.Skyrim);
                    var pxns = pexed.MachineName.Split("-").ToHashSet();
                    if (!pxns.Contains("PXP") && !patched.Contains(file.FileName))
                    {
                        Console.WriteLine($"{file.FileName} is fresh, backing up and using.");
                        Directory.CreateDirectory(Path.Join(state.DataFolderPath, "Scripts", "BAK"));
                        pxns = ["PXP"];
                        File.Copy(scriptName, Path.Join(state.DataFolderPath, "Scripts", "BAK", file.FileName), true);
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
                        //File.WriteAllText(Path.Join(state.ExtraSettingsDataPath, $"{file.FileName}.{pexed.MachineName}.json"), JsonConvert.SerializeObject(pexed, settings));
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
                                foreach (var rewrites in patch.RewriteCode)
                                {
                                    var mth = rewrites.replace.GetMatches(fn.Instructions);
                                    var offset = 0;
                                    foreach (var itm in mth)
                                    {
                                        var rw = rewrites.with.ToList();
                                        fn.Instructions[(offset + itm.Item1)..(offset + itm.Item1 + itm.Item2)].SetTo(rw[..itm.Item2]);
                                        fn.Instructions.InsertRange(offset + itm.Item1 + itm.Item2, rw[itm.Item2..]);
                                        offset += itm.Item2 - rw.Count;
                                        Console.WriteLine($"OFFSET: {offset}");
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
                                        ins.instructions.ForEach(x => x.Transform(sdc));
                                        fn.Instructions.InsertRange(idx, ins.instructions);
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
                                        rw.newInst.Transform(sdc);
                                        inst.OpCode = rw.newInst.OpCode;
                                        inst.Arguments.Clear();
                                        inst.Arguments.SetTo(rw.newInst.Arguments);
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
                    pexed.WritePexFile(scriptName, GameCategory.Skyrim);
                    //File.WriteAllText(Path.Join(state.ExtraSettingsDataPath, $"{file.FileName}.{pexed.MachineName}.json"), JsonConvert.SerializeObject(pexed, settings));
                }
                else
                {
                    Console.WriteLine($"Missing {file.FileName}");
                }
            }
        }
    }
}