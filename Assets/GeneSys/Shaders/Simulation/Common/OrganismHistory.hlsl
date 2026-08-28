#ifndef GENESYS_ORGANISM_HISTORY_INCLUDED
#define GENESYS_ORGANISM_HISTORY_INCLUDED

#ifndef GENESYS_DISABLE_ORGANISM_HISTORY_UAV
RWStructuredBuffer<OrganismHistoryEvent> _OrganismHistory;
RWStructuredBuffer<uint> _OrganismHistoryCounter;

void LogOrganismEvent(uint tick, uint kind, uint generation, uint lineage, uint cause)
{
    uint index;
    InterlockedAdd(_OrganismHistoryCounter[0], 1, index);
    if (index >= ORGANISM_HISTORY_CAPACITY)
        return;
    OrganismHistoryEvent record;
    record.tick = tick;
    record.kind = kind;
    record.generation = generation;
    record.lineage = lineage;
    record.cause = cause;
    record.pad0 = 0u;
    record.pad1 = 0u;
    record.pad2 = 0u;
    _OrganismHistory[index] = record;
}
#else
void LogOrganismEvent(uint tick, uint kind, uint generation, uint lineage, uint cause) { }
#endif

#endif
