using System.Text.Json;
using NeuroCare.Domain;

namespace NeuroCare.Application;

/// <summary>Respostas novas usam a definição registrada; respostas legadas consultam a definição disponível.</summary>
public static class QuestionnaireHistory
{
    public static QuestionnaireDefinition? Snapshot(QuestionnaireResponse response) =>
        string.IsNullOrWhiteSpace(response.DefinitionSnapshotJson)
            ? null
            : JsonSerializer.Deserialize<QuestionnaireDefinition>(response.DefinitionSnapshotJson);
}
