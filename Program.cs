using System.Data;
using Mutagen.Bethesda.Pex;
using Mutagen.Bethesda.Json;
using Newtonsoft.Json;
using Noggog;
using DynamicData;

namespace PapyrusPatch;
internal class Program
{
    static void Main(string[] args)
    {
        var settings = new JsonSerializerSettings();
        settings.AddMutagenConverters();
        Console.WriteLine(Directory.GetCurrentDirectory());
        foreach (var pchFile in Directory.GetFiles(Path.Join("SynPKGs", "PapyrusPatcher")))
        {
            if (!pchFile.EndsWith(".json")) continue;
            var conf = JsonConvert.DeserializeObject<Config>(File.ReadAllText($"{pchFile}"), settings);
            if (conf.pxnName == string.Empty) continue;
            Console.WriteLine($"Running {pchFile}");
            foreach (var file in conf.patches)
            {
                var scriptName = Path.Join("Scripts", file.FileName);
                if (File.Exists(scriptName))
                {
                    Console.WriteLine($"Patching {scriptName}");
                    var pexed = PexFile.CreateFromFile(scriptName, Mutagen.Bethesda.GameCategory.Skyrim);
                    var pxns = pexed.MachineName.Split("-").ToHashSet();
                    if (pxns.Contains(conf.pxnName))
                    {
                        Console.WriteLine($"Papyrus file patched already, skipping {file.FileName}");
                        continue;
                    }
                    pxns.Add(conf.pxnName);
                    pexed.MachineName = string.Join("-", pxns);
                    foreach (var state in file.states)
                    {
                        var obj = pexed.Objects.Where(x => x.Name?.Equals(state.Obj, StringComparison.InvariantCultureIgnoreCase) ?? false).First();
                        var st = obj.States.Where(x => x.Name?.Contains(state.State, StringComparison.InvariantCultureIgnoreCase) ?? false).First();
                        foreach (var patch in state.funcPatch)
                        {
                            var fn = st.Functions.Where(x => x.FunctionName == patch.FunctionName).First().Function;
                            if (patch.temps != null)
                            {
                                fn.Locals.AddRange(patch.temps.GetData());
                            }
                            if (patch.insert != null)
                            {
                                foreach (var ins in patch.insert)
                                {
                                    var pre = fn.Instructions.FindAll(ins.pred.IsInst);
                                    foreach (var loc in pre)
                                    {
                                        var idx = fn.Instructions.IndexOf(loc);
                                        var sdc = ins.pred.GetMatched(fn.Instructions[idx]);
                                        foreach (var inst in ins.instructions)
                                        {
                                            fn.Instructions.Insert(idx++, new PexObjectFunctionInstruction()
                                            {
                                                Arguments = inst.args.GetData(sdc),
                                                OpCode = inst.opCode
                                            });
                                        }
                                    }
                                }
                            }
                            if (patch.rewrite != null)
                            {
                                foreach (var rw in patch.rewrite)
                                {
                                    var instructions = fn.Instructions.FindAll(rw.pred.IsInst);
                                    foreach (var inst in instructions)
                                    {
                                        var sdc = rw.pred.GetMatched(inst);
                                        inst.Arguments.Clear();
                                        inst.OpCode = rw.newInst;
                                        inst.Arguments.AddRange(rw.args.GetData(sdc));
                                    }
                                }
                            }
                            if (patch.rwArgs != null)
                            {
                                foreach (var rw in patch.rwArgs)
                                {
                                    var pre = fn.Instructions.FindAll(rw.pred.IsInst);
                                    foreach (var loc in pre)
                                    {
                                        var rc = fn.Instructions.IndexOf(loc);
                                        var instruction = fn.Instructions[rc];
                                        foreach (var arg in rw.args)
                                        {
                                            instruction.Arguments[arg.index] = arg.dat.GetData();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    pexed.WritePexFile(scriptName, Mutagen.Bethesda.GameCategory.Skyrim);
                }
                else
                {
                    Console.WriteLine($"Missing {scriptName}");
                }
            }
        }
        Console.WriteLine("Press any key to exit");
        Console.In.Read();
    }
}