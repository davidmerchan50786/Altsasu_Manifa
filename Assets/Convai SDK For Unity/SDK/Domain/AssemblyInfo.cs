using System.Runtime.CompilerServices;

// Allow Runtime (which now includes Application layer) to access internal members
[assembly: InternalsVisibleTo("Convai.Runtime")]

// Allow test assemblies to access internal members for unit testing
[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]
[assembly: InternalsVisibleTo("Convai.Tests.PlayMode")]
