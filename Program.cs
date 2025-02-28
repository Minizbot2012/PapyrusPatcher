using System.Data;
using Mutagen.Bethesda.Pex;
using Mutagen.Bethesda.Json;
using Newtonsoft.Json;
using Noggog;

namespace PapyrusPatch;
internal class Program
{
    static void Main(string[] args)
    {
        var patchFile = "Patch.json";
        if(args.Length >= 1) {
            patchFile = args[0];
        }
        var settings = new JsonSerializerSettings();
        settings.AddMutagenConverters();
        var conf = JsonConvert.DeserializeObject<Config>(File.ReadAllText(patchFile), settings);
        if (conf == null) return;
        foreach (var file in conf.patches)
        {
            var pexed = PexFile.CreateFromFile($"Scripts/{file.FileName}", Mutagen.Bethesda.GameCategory.Skyrim);
            if(pexed.MachineName == "PAPPATCHED") 
            {
                Console.WriteLine($"Papyrus file patched already, skipping {file.FileName}");
                continue;
            }
            pexed.MachineName = "PAPPATCHED";
            foreach (var state in file.states)
            {
                var obj = pexed.Objects.Where(x => x.Name == state.Obj).First();
                var st = obj.States.Where(x => x.Name == state.State).First();
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
                            var pre = fn.Instructions.FindIndex(ins.pred.IsInst);
                            foreach (var inst in ins.instructions)
                            {
                                fn.Instructions.Insert(pre, new PexObjectFunctionInstruction()
                                {
                                    Arguments = inst.args.GetData(),
                                    OpCode = inst.opCode
                                });
                                pre++;
                            }
                        }
                    }
                    if (patch.rewrite != null)
                    {
                        foreach (var rw in patch.rewrite)
                        {
                            var pre = fn.Instructions.FindIndex(rw.pred.IsInst);
                            var instruction = fn.Instructions[pre];
                            instruction.Arguments.Clear();
                            instruction.OpCode = rw.newInst;
                            instruction.Arguments.AddRange(rw.args.GetData());
                        }
                    }
                    if (patch.rwArgs != null)
                    {
                        foreach (var rw in patch.rwArgs)
                        {
                            var pre = fn.Instructions.FindIndex(rw.pred.IsInst);
                            var instruction = fn.Instructions[pre];
                            foreach (var arg in rw.args)
                            {
                                instruction.Arguments[arg.index] = arg.dat.GetData();
                            }
                        }
                    }
                }
            }
            pexed.WritePexFile($"Scripts/{file.FileName}", Mutagen.Bethesda.GameCategory.Skyrim);
        }
    }
}
