#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Client.Packets.Programmator;
using MinesServer.Networking.Server.Packets.Programmator;
using MinesServer.Data;
using VContainer.Unity;

namespace Kern.Networking.Processing;

// Owns the editor <-> VM protocol. The UI may be rebuilt with the scene, while
// this service keeps packet subscriptions scoped to the game lifetime.
public sealed class ProgrammatorProcessor : IStartable, IDisposable
{
    private readonly INetworkService _networkService;
    public ProgrammatorProcessor(INetworkService networkService)
    {
        _networkService = networkService;
    }

    private bool _subscribed;

    public event Action<UpdateProgramPacket>? ProgramUpdated;
    public event Action<ProgramStatePacket>? StateChanged;
    public event Action<BreakpointHitPacket>? BreakpointHit;
    public event Action<ProgramMemoryPacket>? MemoryReceived;
    public event Action? OpenRequested;

    public void Start()
    {
        if (_subscribed)
        {
            return;
        }

        _networkService.Subscribe<UpdateProgramPacket>(OnProgramUpdated);
        _networkService.Subscribe<ProgramStatePacket>(OnStateChanged);
        _networkService.Subscribe<BreakpointHitPacket>(OnBreakpointHit);
        _networkService.Subscribe<ProgramMemoryPacket>(OnMemoryReceived);
        _networkService.Subscribe<OpenProgrammatorPacket>(OnOpenRequested);
        _subscribed = true;
    }

    public void Dispose()
    {
        if (!_subscribed)
        {
            return;
        }

        _networkService.Unsubscribe<UpdateProgramPacket>(OnProgramUpdated);
        _networkService.Unsubscribe<ProgramStatePacket>(OnStateChanged);
        _networkService.Unsubscribe<BreakpointHitPacket>(OnBreakpointHit);
        _networkService.Unsubscribe<ProgramMemoryPacket>(OnMemoryReceived);
        _networkService.Unsubscribe<OpenProgrammatorPacket>(OnOpenRequested);
        _subscribed = false;
    }

    public void Save(
        int programId,
        bool saveAndRun,
        IReadOnlyList<(ProgAction Operator, string Label, string Value)> program,
        int[] breakpoints)
    {
        _networkService.Send(new SaveProgramPacket(programId, saveAndRun, program, breakpoints));
    }

    public void StartProgram() => _networkService.Send(new StartProgramPacket());

    public void PauseProgram() => _networkService.Send(new PauseProgramPacket());

    public void StopProgram() => _networkService.Send(new StopProgramPacket());

    public void StepIn() => _networkService.Send(new ProgramStepInPacket());

    public void StepOut() => _networkService.Send(new ProgramStepOutPacket());

    public void StepOver() => _networkService.Send(new ProgramStepOverPacket());

    public void DeleteProgram(int programId) => _networkService.Send(new DeleteProgramClickPacket(programId));

    // Имя вводится в серверном окне (StaticGUI.Rename): пакет только адресует программу.
    public void RenameProgram(int programId) => _networkService.Send(new RenameProgramClickPacket(programId));

    // Кнопка «Программы» в HUD и «К списку» в редакторе (G7-C): источник списка
    // — сервер. Ответ сервера — OpenProgrammatorPacket (грид открывается по
    // OpenRequested), выбор программы ходит ElementClickPacket'ом openprog:{i}.
    // DummyConnection отвечает тем же пакетом — путь един для сети и оффлайна.
    public void RequestProgramList() => _networkService.Send(new OpenProgramListClickPacket());

    public void QueryMemory(IReadOnlyList<string> variables, ushort arrayStart, ushort arrayStop) =>
        _networkService.Send(new QueryProgramMemoryPacket(variables, arrayStart, arrayStop));

    private void OnProgramUpdated(UpdateProgramPacket packet) => ProgramUpdated?.Invoke(packet);

    private void OnStateChanged(ProgramStatePacket packet) => StateChanged?.Invoke(packet);

    private void OnBreakpointHit(BreakpointHitPacket packet) => BreakpointHit?.Invoke(packet);

    private void OnMemoryReceived(ProgramMemoryPacket packet) => MemoryReceived?.Invoke(packet);

    private void OnOpenRequested(OpenProgrammatorPacket packet) => OpenRequested?.Invoke();
}
