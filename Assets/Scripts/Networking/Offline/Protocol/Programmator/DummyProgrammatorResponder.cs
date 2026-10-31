#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Programmator;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Programmator;

namespace Kern.Networking.Offline;

internal sealed class DummyProgrammatorResponder
{
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Dictionary<int, ProgramRecord> _programs = new();
    private ProgramState _state = ProgramState.Stopped;
    private int _activeProgramId = -1;

    public DummyProgrammatorResponder(Action<ServerPacket> sendPacket)
    {
        _sendPacket = sendPacket ?? throw new ArgumentNullException(nameof(sendPacket));
    }

    public void Save(SaveProgramPacket packet)
    {
        ProgramRecord record = new(
            packet.Program.ToArray(),
            (int[])packet.Breakpoints.Clone());
        _programs[packet.ProgramId] = record;
        _activeProgramId = packet.ProgramId;
        PublishProgram(packet.ProgramId, record);
        SetState(packet.SaveAndRun ? ProgramState.Running : ProgramState.Stopped);
    }

    public void Start()
    {
        SetState(ProgramState.Running);
        if (_programs.TryGetValue(_activeProgramId, out ProgramRecord record) &&
            record.Breakpoints.Length > 0)
        {
            _sendPacket(new ServerPacket(new BreakpointHitPacket(record.Breakpoints)));
            SetState(ProgramState.Paused);
        }
    }

    public void Pause()
    {
        SetState(ProgramState.Paused);
    }

    public void Stop()
    {
        SetState(ProgramState.Stopped);
    }

    public void Step()
    {
        SetState(ProgramState.Paused);
    }

    public void Delete()
    {
        if (_activeProgramId >= 0)
        {
            _programs.Remove(_activeProgramId);
        }

        _activeProgramId = -1;
        SetState(ProgramState.Stopped);
    }

    public void QueryMemory(QueryProgramMemoryPacket packet)
    {
        int length = packet.ArrayStop >= packet.ArrayStart
            ? packet.ArrayStop - packet.ArrayStart + 1
            : 0;
        _sendPacket(new ServerPacket(new ProgramMemoryPacket(
            new int[packet.Variables.Count],
            new int[length])));
    }

    private void PublishProgram(int programId, ProgramRecord record)
    {
        _sendPacket(new ServerPacket(new UpdateProgramPacket(
            programId,
            $"Program {programId + 1}",
            record.Instructions,
            record.Breakpoints)));
    }

    // Оффлайн-отражение серверного контракта G3-S (ProgramStatus.Resolve
    // на NS): движение разрешено только вне Running — на паузе/брейкпоинте
    // клиент ходит и копает, под работающей программой ввод закрыт.
    private void SetState(ProgramState state)
    {
        _state = state;
        _sendPacket(new ServerPacket(new ProgramStatePacket(_state, state != ProgramState.Running)));
    }

    private sealed record ProgramRecord(
        IReadOnlyList<(ProgAction Operator, string Label, string Value)> Instructions,
        int[] Breakpoints);
}
