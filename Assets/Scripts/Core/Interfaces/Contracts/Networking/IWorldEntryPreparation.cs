#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Kern.Core.Interfaces;

// То, что должно быть готово до перехода в игровую сцену: переход меряет
// таймаутом только загрузку самой сцены, долгая подготовка в него не входит.
public interface IWorldEntryPreparation
{
    UniTask EnsureReadyAsync(CancellationToken cancellationToken);
}
