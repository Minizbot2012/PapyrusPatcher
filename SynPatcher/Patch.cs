using Mutagen.Bethesda.Pex;
using Noggog;

namespace SynPatcher;

struct IndexedVarData
{
    public int index;
    public PexObjectVariableData data;
}

struct InstMatch
{
    public InstructionOpcode OpCode;
    public IEnumerable<IndexedVarData> Arguments;
    public readonly bool IsInst(PexObjectFunctionInstruction inst)
    {
        if (inst.Arguments.Count <= Arguments.Max(x => x.index)) return false;
        if (inst.OpCode != OpCode) return false;
        return Arguments.All(x =>
        {
            return (x.data.VariableType == inst.Arguments[x.index].VariableType && x.data.BoolValue == inst.Arguments[x.index].BoolValue && x.data.IntValue == inst.Arguments[x.index].IntValue && x.data.StringValue == inst.Arguments[x.index].StringValue && x.data.FloatValue == inst.Arguments[x.index].FloatValue) || (x.data.VariableType == VariableType.Null && x.data.StringValue != null);
        });
    }
    public readonly Dictionary<string, PexObjectVariableData> GetMatched(PexObjectFunctionInstruction inst)
    {
        Dictionary<string, PexObjectVariableData> dict = [];
        foreach (var x in Arguments)
        {
            if (x.data.VariableType == VariableType.Null && x.data.StringValue != null)
            {
                dict[x.data.StringValue] = inst.Arguments[x.index];
            }
        }
        return dict;
    }
}

static class Extensions
{
    public static Dictionary<string, PexObjectVariableData> GetMatched(this IEnumerable<PexObjectFunctionInstruction> insts)
    {
        Dictionary<string, PexObjectVariableData> dict = [];
        foreach (var arg in insts.SelectMany(x => x.Arguments.Where(x => x.VariableType == VariableType.Null && x.StringValue != null).ToList()))
        {
            if (arg.StringValue != null)
            {
                if (dict.ContainsKey(arg.StringValue!))
                {
                    throw new Exception("Error duplicate key in matcher");
                }
                else
                {
                    dict[arg.StringValue] = arg.DeepCopy();
                }
            }
        }
        return dict;
    }
    public static IEnumerable<(int, int)> GetMatches(this IEnumerable<InstMatch> matcher, IEnumerable<PexObjectFunctionInstruction> instructions)
    {
        var mlen = matcher.Count();
        var match = matcher.ToList();
        var instructs = instructions.ToList();
        var list = new HashSet<(int, int)>();
        for (int i = 0; i < instructs.Count - mlen; i++)
        {
            var sub = instructs[i..(i + mlen)];
            if (match.Zip(sub).All(x => x.First.IsInst(x.Second)))
            {
                list.Add((i, mlen));
            }
        }
        return list;
    }
    public static PexObjectFunctionInstruction Transform(this PexObjectFunctionInstruction inst, Dictionary<string, PexObjectVariableData> srd)
    {
        var newInst = inst.DeepCopy();
        var margs = inst.Arguments.Where(x => x.StringValue != null && x.VariableType == VariableType.Null);
        foreach (var arg in margs)
        {
            newInst.Arguments[inst.Arguments.IndexOf(arg)] = srd[arg.StringValue!];
        }
        return newInst;
    }
    public static IEnumerable<PexObjectFunctionInstruction> TransformList(this IEnumerable<PexObjectFunctionInstruction> instructions, Dictionary<string, PexObjectVariableData> srd)
    {
        return instructions.Select(x => x.Transform(srd));
    }
}

struct RewriteInstruction
{
    public InstMatch pred;
    public PexObjectFunctionInstruction newInst;
}

struct RewriteArgs
{
    public InstMatch pred;
    public IEnumerable<IndexedVarData> Arguments;
}

struct RewriteCode
{
    public IEnumerable<InstMatch> replace;
    public IEnumerable<PexObjectFunctionInstruction> with;
}

struct InsertInstruction
{
    public InstMatch pred;
    public IEnumerable<PexObjectFunctionInstruction> instructions;
    public readonly IEnumerable<PexObjectVariableData> GetData() => instructions.SelectMany(x => x.Arguments);
}

struct Patch
{
    public string FunctionName;
    public IEnumerable<PexObjectFunctionVariable>? NewLocals;
    public IEnumerable<RewriteCode>? RewriteCode;
    public IEnumerable<InsertInstruction>? InsertInstructions;
    public IEnumerable<RewriteInstruction>? RewriteInstruction;
    public IEnumerable<RewriteArgs>? RewriteArgs;
}

struct PatchState
{
    public string State;
    public string Obj;
    public IEnumerable<Patch> funcPatch;
}

struct PatchFile
{
    public string FileName;
    public IEnumerable<PatchState> states;
}
struct Config
{
    public string pxnName;
    public IEnumerable<PatchFile> patches;
}