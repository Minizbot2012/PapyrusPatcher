using System.Text.RegularExpressions;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Pex;
using Noggog;

namespace PapyrusPatch;
public struct VarData
{
    public VariableType? VarType;
    public string? SaveRecall;
    public string? StringData;
    public int? IntData;
    public bool? BoolData;
    public float? FloatData;
    public PexObjectVariableData GetData()
    {
        return VarType switch
        {
            VariableType.Null => new()
            {
                VariableType = VariableType.Null
            },
            VariableType.Identifier or VariableType.String => new()
            {
                VariableType = (VariableType)VarType,
                StringValue = StringData,
            },
            VariableType.Integer => new()
            {
                VariableType = VariableType.Integer,
                IntValue = IntData,
            },
            VariableType.Bool => new()
            {
                VariableType = VariableType.Bool,
                BoolValue = BoolData
            },
            VariableType.Float => new()
            {
                VariableType = VariableType.Float,
                FloatValue = FloatData
            },
            _ => new()
            {
                VariableType = VariableType.Null
            },
        };
    }
}

public static class ListExts
{
    public static ExtendedList<PexObjectVariableData> GetData(this IEnumerable<VarData> data, IDictionary<string, PexObjectVariableData> sd)
    {
        ExtendedList<PexObjectVariableData> dat = [];
        foreach (var inf in data)
        {
            if (inf.SaveRecall != null)
            {
                dat.Add(sd[inf.SaveRecall]);
            }
            else
            {
                dat.Add(inf.GetData());
            }
        }
        return dat;
    }
    public static ExtendedList<PexObjectFunctionVariable> GetData(this IEnumerable<NewTemps> data)
    {
        ExtendedList<PexObjectFunctionVariable> dat = [];
        foreach (var inf in data)
        {
            dat.Add(inf.GetData());
        }
        return dat;
    }
}

struct IndexedVarData
{
    public int index;
    public VarData dat;
}

struct InstMatch
{
    public InstructionOpcode opCode;
    public IEnumerable<IndexedVarData> data;
    public readonly bool IsInst(PexObjectFunctionInstruction inst)
    {
        if (inst.Arguments.Count <= data.Max(x => x.index)) return false;
        return data.All(x =>
        {
            if (x.dat.VarType != null)
            {
                var arg = inst.Arguments[x.index];
                if (arg.VariableType == x.dat.VarType)
                {
                    return arg.VariableType switch
                    {
                        VariableType.Null => true,
                        VariableType.Identifier or VariableType.String => new Regex(x.dat.StringData ?? "NULL1").IsMatch(arg.StringValue ?? "NULL2"),
                        VariableType.Integer => arg.IntValue == x.dat.IntData,
                        VariableType.Bool => arg.BoolValue == x.dat.BoolData,
                        _ => true,
                    };
                }
            }
            else if (x.dat.SaveRecall != null)
            {
                return true;
            }
            return false;
        });
    }
    public readonly Dictionary<string, PexObjectVariableData> GetMatched(PexObjectFunctionInstruction inst)
    {
        Dictionary<string, PexObjectVariableData> dict = [];
        foreach (var x in data)
        {
            if (x.dat.SaveRecall != null)
            {
                dict[x.dat.SaveRecall] = inst.Arguments[x.index];
            }
        }
        return dict;
    }
}

struct RewriteInstruction
{
    public InstMatch pred;
    public InstructionOpcode newInst;
    public IEnumerable<VarData> args;
}

struct RewriteArgs
{
    public InstMatch pred;
    public IEnumerable<IndexedVarData> args;
}

public struct NewTemps
{
    public string TypeName;
    public string Name;
    public PexObjectFunctionVariable GetData()
    {
        return new()
        {
            Name = Name,
            TypeName = TypeName,
        };
    }
}

struct InstructionList
{
    public InstructionOpcode opCode;
    public List<VarData> args;
}

struct InsertInstruction
{
    public InstMatch pred;
    public IEnumerable<InstructionList> instructions;
}

struct Patch
{
    public string FunctionName;
    public IEnumerable<NewTemps>? temps;
    public IEnumerable<InsertInstruction>? insert;
    public IEnumerable<RewriteInstruction>? rewrite;
    public IEnumerable<RewriteArgs>? rwArgs;
}

struct PatchState
{
    public string State;
    public string Obj;
    public List<Patch> funcPatch;
}

struct PatchFile
{
    public string FileName;
    public List<PatchState> states;
}
struct Config
{
    public string pxnName;
    public List<PatchFile> patches;
}