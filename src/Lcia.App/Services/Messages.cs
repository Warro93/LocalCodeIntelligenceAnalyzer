namespace Lcia.App.Services;

/// <summary>Richiesta di aprire un file del repository corrente nell'Esplora, alla riga indicata.</summary>
public sealed record OpenFileMessage(string RelativePath, int Line);
