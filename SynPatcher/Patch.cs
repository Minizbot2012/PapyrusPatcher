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
            return x.data == inst.Arguments[x.index] || (x.data.VariableType == VariableType.Null && x.data.StringValue != null);
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
    public static void Transform(this PexObjectFunctionInstruction inst, Dictionary<string, PexObjectVariableData> srd)
    {
        var margs = inst.Arguments.Where(x => x.StringValue != null && x.VariableType == VariableType.Null);
        foreach (var arg in margs)
        {
            inst.Arguments[inst.Arguments.IndexOf(arg)] = srd[arg.StringValue!];
        }
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