using System;
using System.Globalization;
using System.Text;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Simulation.Topology;
using GeneSys.Tools;
using GeneSys.Validation;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GeneSys.AI
{
    public static class AiBuiltinTools
    {
        public static void RegisterAll(AiToolRegistry registry)
        {
            if (registry == null) return;
            RegisterMeta(registry);
            RegisterSensors(registry);
            RegisterProbe(registry);
            RegisterDeity(registry);
        }

        private static void RegisterMeta(AiToolRegistry registry)
        {
            registry.Add(new AiTool
            {
                Name = "next_step",
                Description = "Finish the current ACT stage and advance Assess -> Convert -> Think, or end the loop after Think.",
                Parameters = AiToolRegistry.EmptyObjectSchema(),
                AllowedSteps = ActStepMask.All,
                Handler = (_, done) =>
                {
                    registry.Context?.RequestNextStep?.Invoke();
                    done?.Invoke("Advanced to the next ACT step.");
                }
            });
            registry.Add(new AiTool
            {
                Name = "scratchpad_read",
                Description = "Read your persistent notes.",
                Parameters = AiToolRegistry.EmptyObjectSchema(),
                AllowedSteps = ActStepMask.All,
                Handler = (_, done) =>
                {
                    string text = registry.Context?.Scratchpad?.Read() ?? string.Empty;
                    done?.Invoke(string.IsNullOrWhiteSpace(text) ? "(scratchpad empty)" : text);
                }
            });
            registry.Add(new AiTool
            {
                Name = "scratchpad_write",
                Description = "Replace the persistent scratchpad with the given content.",
                Parameters = AiToolRegistry.ObjectSchema(("content", AiToolRegistry.StringProp("Full scratchpad text."), true)),
                AllowedSteps = ActStepMask.All,
                Handler = (args, done) =>
                {
                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    registry.Context?.Scratchpad?.Write(AiToolRegistry.ArgString(parsed, "content"));
                    done?.Invoke("Scratchpad updated.");
                }
            });
            registry.Add(new AiTool
            {
                Name = "log_note",
                Description = "Append a freeform note to the deterministic action log.",
                Parameters = AiToolRegistry.ObjectSchema(("text", AiToolRegistry.StringProp("Note to record."), true)),
                AllowedSteps = ActStepMask.All,
                Handler = (args, done) =>
                {
                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    string text = AiToolRegistry.ArgString(parsed, "text");
                    AiToolContext ctx = registry.Context;
                    ctx?.ActionLog?.Record(ctx.Host != null ? ctx.Host.Clock.TickCount : 0L, ctx.Step, "log_note", args, text);
                    done?.Invoke("Noted.");
                }
            });
            registry.Add(new AiTool
            {
                Name = "send_chat",
                Description = "Send a player-facing reply in the probe chat. Use during Convert of a player-message ACT loop.",
                Parameters = AiToolRegistry.ObjectSchema(("text", AiToolRegistry.StringProp("Message shown to the player."), true)),
                AllowedSteps = ActStepMask.Convert,
                UserChatReply = true,
                Handler = (args, done) =>
                {
                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    string text = AiToolRegistry.ArgString(parsed, "text");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        done?.Invoke("send_chat requires non-empty text.");
                        return;
                    }

                    registry.Context?.SendChat?.Invoke(text.Trim());
                    done?.Invoke("Message sent to the player.");
                }
            });
        }

        private static void RegisterSensors(AiToolRegistry registry)
        {
            registry.Add(new AiTool
            {
                Name = "get_planet_summary",
                Description = "Read mean temperature, humidity, water masses, fire, and organism counts.",
                Parameters = AiToolRegistry.EmptyObjectSchema(),
                AllowedSteps = ActStepMask.Assess | ActStepMask.Think,
                Handler = (_, done) =>
                {
                    SimulationHost host = registry.Context?.Host;
                    if (host == null || !host.IsReady)
                    {
                        done?.Invoke("Simulation is not ready.");
                        return;
                    }

                    SimulationMetrics.MeasureAsync(host, metrics => done?.Invoke(FormatPlanet(metrics)));
                }
            });
            registry.Add(new AiTool
            {
                Name = "get_species_metrics",
                Description = "Read living counts for flora, crickets, wasps, or trees.",
                Parameters = AiToolRegistry.ObjectSchema(("species", AiToolRegistry.StringProp("Species to measure.", "flora", "cricket", "wasp", "tree"), true)),
                AllowedSteps = ActStepMask.Assess | ActStepMask.Think,
                Handler = (args, done) =>
                {
                    SimulationHost host = registry.Context?.Host;
                    if (host == null || !host.IsReady)
                    {
                        done?.Invoke("Simulation is not ready.");
                        return;
                    }

                    string species = AiToolRegistry.ArgString(AiToolRegistry.ParseArgs(args), "species", "flora").ToLowerInvariant();
                    switch (species)
                    {
                        case "flora":
                        case "algae":
                            SimulationMetrics.MeasureFloraAsync(host, m => done?.Invoke(
                                $"flora living={m.LivingCount} dormant={m.DormantCount} desiccated={m.DesiccatedCount} biomass={m.TotalBiomass:0.###} energy={m.MeanEnergy:0.###}"));
                            break;
                        case "cricket":
                        case "fauna":
                            SimulationMetrics.MeasureFaunaAsync(host, m => done?.Invoke(
                                $"crickets adult={m.AdultCount} juvenile={m.JuvenileCount} eggs={m.EggCount} cal={m.TotalCalories:0.###} hyd={m.TotalHydration:0.###}"));
                            break;
                        case "wasp":
                            SimulationMetrics.MeasureWaspAsync(host, m => done?.Invoke(
                                $"wasps adult={m.AdultCount} juvenile={m.JuvenileCount} eggs={m.EggCount} pollenCarriers={m.PollenCarrierCount}"));
                            break;
                        case "tree":
                            SimulationMetrics.MeasureTreeAsync(host, m => done?.Invoke(
                                $"trees anchors={m.AnchorCount} sprout={m.SproutCount} sapling={m.SaplingCount} mature={m.TreeCount} dead={m.DeadCount}"));
                            break;
                        default:
                            done?.Invoke("Unknown species. Use flora, cricket, wasp, or tree.");
                            break;
                    }
                }
            });
            registry.Add(new AiTool
            {
                Name = "inspect_cell",
                Description = "Read one polar cell. theta01 is 0..1 around the circle; radius01 is 0 at the core and 1 at the rim.",
                Parameters = AiToolRegistry.ObjectSchema(
                    ("theta01", AiToolRegistry.NumberProp("Angular position 0..1."), true),
                    ("radius01", AiToolRegistry.NumberProp("Radial position 0..1."), true)),
                AllowedSteps = ActStepMask.Assess | ActStepMask.Think,
                Handler = (args, done) =>
                {
                    AiToolContext ctx = registry.Context;
                    if (ctx?.Host == null || !ctx.Host.IsReady)
                    {
                        done?.Invoke("Simulation is not ready.");
                        return;
                    }

                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    Vector2Int cell = PolarCellMapping.FromNormalized(ctx.Host.Grid,
                        AiToolRegistry.ArgFloat(parsed, "theta01"), AiToolRegistry.ArgFloat(parsed, "radius01"));
                    if (ctx.Tools == null)
                    {
                        done?.Invoke($"cell=({cell.x},{cell.y}) but inspection tools are unavailable.");
                        return;
                    }

                    ctx.Tools.RequestInspection(cell, inspection => done?.Invoke(FormatInspection(inspection)));
                }
            });
            registry.Add(new AiTool
            {
                Name = "get_recent_organism_events",
                Description = "Return the latest organism history lines (birth, death, reproduce).",
                Parameters = AiToolRegistry.ObjectSchema(("count", AiToolRegistry.IntegerProp("How many recent events to return."), false)),
                AllowedSteps = ActStepMask.Assess | ActStepMask.Think,
                Handler = (args, done) =>
                {
                    SimulationHost host = registry.Context?.Host;
                    if (host == null)
                    {
                        done?.Invoke("No simulation host.");
                        return;
                    }

                    int count = Mathf.Clamp(AiToolRegistry.ArgInt(AiToolRegistry.ParseArgs(args), "count", 16), 1, 64);
                    var entries = host.OrganismHistory.Entries;
                    if (entries.Count == 0)
                    {
                        done?.Invoke("No organism events yet.");
                        return;
                    }

                    int start = Mathf.Max(0, entries.Count - count);
                    var builder = new StringBuilder();
                    for (int i = start; i < entries.Count; i++)
                    {
                        if (builder.Length > 0) builder.Append('\n');
                        builder.Append(entries[i].Format());
                    }

                    done?.Invoke(builder.ToString());
                }
            });
            registry.Add(new AiTool
            {
                Name = "capture_probe_view",
                Description = "Capture a JPEG screenshot of a hidden probe-follow camera at 50% zoom. The image is attached to the next model request. Use when visual context would help Assess or Think.",
                Parameters = AiToolRegistry.EmptyObjectSchema(),
                AllowedSteps = ActStepMask.Assess | ActStepMask.Think,
                RequiresVision = true,
                Handler = (_, done) =>
                {
                    PlanetoidDisplayRenderer display = registry.Context?.Display;
                    if (display == null)
                    {
                        done?.Invoke("Display renderer is unavailable.");
                        return;
                    }

                    const int width = 768;
                    const int height = 512;
                    if (!display.TryCaptureProbeFollowVision(width, height, out byte[] jpeg, out string error))
                    {
                        done?.Invoke(error ?? "Vision capture failed.");
                        return;
                    }

                    registry.Context.OnVisionFrame?.Invoke(jpeg, width, height);
                    done?.Invoke($"Captured probe-follow vision frame ({jpeg.Length} bytes JPEG, {width}x{height}, 50% zoom). The image is attached for the next model request.");
                }
            });
        }

        private static void RegisterProbe(AiToolRegistry registry)
        {
            registry.Add(new AiTool
            {
                Name = "probe_steer",
                Description = "Set probe flight: clockwise, counterclockwise, or stopped.",
                Parameters = AiToolRegistry.ObjectSchema(("direction", AiToolRegistry.StringProp("Flight mode.", "clockwise", "counterclockwise", "stopped"), true)),
                AllowedSteps = ActStepMask.Convert,
                Handler = (args, done) =>
                {
                    ProbeController probe = registry.Context?.Probe;
                    if (probe == null)
                    {
                        done?.Invoke("Probe is unavailable.");
                        return;
                    }

                    string direction = AiToolRegistry.ArgString(AiToolRegistry.ParseArgs(args), "direction").ToLowerInvariant();
                    ProbeFlightMode mode = direction switch
                    {
                        "counterclockwise" or "ccw" or "left" => ProbeFlightMode.Counterclockwise,
                        "stopped" or "stop" => ProbeFlightMode.Stopped,
                        _ => ProbeFlightMode.Clockwise
                    };
                    probe.SetFlightMode(mode);
                    done?.Invoke($"Probe flight set to {mode}.");
                }
            });
            registry.Add(new AiTool
            {
                Name = "probe_use_tool",
                Description = "Hold a probe deposit tool for duration_seconds, then release.",
                Parameters = AiToolRegistry.ObjectSchema(
                    ("tool", AiToolRegistry.StringProp("Probe tool.", "humidity", "water", "soil", "cool", "heat"), true),
                    ("duration_seconds", AiToolRegistry.NumberProp("Hold duration in seconds."), false)),
                AllowedSteps = ActStepMask.Convert,
                Handler = (args, done) =>
                {
                    AiToolContext ctx = registry.Context;
                    ProbeController probe = ctx?.Probe;
                    if (probe == null)
                    {
                        done?.Invoke("Probe is unavailable.");
                        return;
                    }

                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    if (!TryParseProbeAction(AiToolRegistry.ArgString(parsed, "tool"), out ProbeAction action))
                    {
                        done?.Invoke("Unknown probe tool. Use humidity, water, soil, cool, or heat.");
                        return;
                    }

                    float duration = Mathf.Clamp(AiToolRegistry.ArgFloat(parsed, "duration_seconds", 1.5f), 0.1f, 30f);
                    probe.SetAction(action);
                    if (ctx.StartRoutine == null)
                    {
                        probe.SetAction(ProbeAction.None);
                        done?.Invoke($"Applied {action} without a timed hold.");
                        return;
                    }

                    ctx.StartRoutine(ReleaseProbeAfter(probe, action, duration, done));
                }
            });
            registry.Add(new AiTool
            {
                Name = "probe_toggle_life_seed",
                Description = "Turn mixed life-seed bursts on or off.",
                Parameters = AiToolRegistry.ObjectSchema(("on", AiToolRegistry.BoolProp("True to enable life seed."), true)),
                AllowedSteps = ActStepMask.Convert,
                Handler = (args, done) =>
                {
                    ProbeController probe = registry.Context?.Probe;
                    if (probe == null)
                    {
                        done?.Invoke("Probe is unavailable.");
                        return;
                    }

                    bool on = AiToolRegistry.ArgBool(AiToolRegistry.ParseArgs(args), "on");
                    probe.SetLifeSeedActive(on);
                    done?.Invoke(on ? "Life seed enabled." : "Life seed disabled.");
                }
            });
            registry.Add(new AiTool
            {
                Name = "probe_status",
                Description = "Read probe angle, energy, flight mode, active tool, and aim cell.",
                Parameters = AiToolRegistry.EmptyObjectSchema(),
                AllowedSteps = ActStepMask.All,
                Handler = (_, done) =>
                {
                    AiToolContext ctx = registry.Context;
                    ProbeController probe = ctx?.Probe;
                    if (probe == null)
                    {
                        done?.Invoke("Probe is unavailable.");
                        return;
                    }

                    PolarGridDefinition grid = ctx.Host != null ? ctx.Host.Grid : default;
                    Vector2Int aim = probe.AimCell(grid);
                    done?.Invoke(
                        $"angle01={probe.ProbeAngle01:0.###} energy={probe.EnergyNormalized:0.###} flight={probe.FlightMode} " +
                        $"action={probe.ActiveAction} lifeSeed={probe.LifeSeedActive} aim=({aim.x},{aim.y})");
                }
            });
        }

        private static void RegisterDeity(AiToolRegistry registry)
        {
            registry.Add(new AiTool
            {
                Name = "planet_adjust_field",
                Description = "Planetwide field adjust. channel: heat, cool, water, humidity, nutrients, groundwater, pressure.",
                Parameters = AiToolRegistry.ObjectSchema(
                    ("channel", AiToolRegistry.StringProp("Field to adjust.", "heat", "cool", "water", "humidity", "nutrients", "groundwater", "pressure"), true),
                    ("amount", AiToolRegistry.NumberProp("Signed amount added to every cell."), true)),
                AllowedSteps = ActStepMask.Convert,
                RequiresDeity = true,
                Handler = (args, done) =>
                {
                    SimulationHost host = registry.Context?.Host;
                    if (host == null || !host.IsReady)
                    {
                        done?.Invoke("Simulation is not ready.");
                        return;
                    }

                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    if (!TryParseFieldChannel(AiToolRegistry.ArgString(parsed, "channel"),
                            AiToolRegistry.ArgFloat(parsed, "amount"), out int channel, out float amount, out string error))
                    {
                        done?.Invoke(error);
                        return;
                    }

                    host.QueueGlobalAdjust(channel, amount);
                    done?.Invoke($"Queued planetwide channel {channel} adjust of {amount.ToString("0.###", CultureInfo.InvariantCulture)}.");
                }
            });
            registry.Add(new AiTool
            {
                Name = "set_world_parameter",
                Description = "Set a whitelisted SimulationConfig rate or threshold. Takes effect next tick.",
                Parameters = AiToolRegistry.ObjectSchema(
                    ("name", AiToolRegistry.StringProp("Config field name.", WorldParameterCatalog.Names), true),
                    ("value", AiToolRegistry.NumberProp("New value."), true)),
                AllowedSteps = ActStepMask.Convert,
                RequiresDeity = true,
                Handler = (args, done) =>
                {
                    SimulationHost host = registry.Context?.Host;
                    if (host?.Config == null)
                    {
                        done?.Invoke("No simulation config.");
                        return;
                    }

                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    WorldParameterCatalog.TrySet(host.Config, AiToolRegistry.ArgString(parsed, "name"),
                        AiToolRegistry.ArgFloat(parsed, "value"), out string message);
                    done?.Invoke(message);
                }
            });
            registry.Add(new AiTool
            {
                Name = "terraform",
                Description = "Paint a material or deposit a field in a radius around a polar cell.",
                Parameters = AiToolRegistry.ObjectSchema(
                    ("theta01", AiToolRegistry.NumberProp("Angular position 0..1."), true),
                    ("radius01", AiToolRegistry.NumberProp("Radial position 0..1."), true),
                    ("brush_radius", AiToolRegistry.IntegerProp("Brush radius in cells."), false),
                    ("material", AiToolRegistry.StringProp("Optional material to paint.", "soil", "water", "sediment", "ice", "ash", "air"), false),
                    ("channel", AiToolRegistry.StringProp("Optional field channel instead of paint.", "heat", "cool", "water", "humidity", "nutrients", "groundwater"), false),
                    ("amount", AiToolRegistry.NumberProp("Deposit amount when using a channel."), false)),
                AllowedSteps = ActStepMask.Convert,
                RequiresDeity = true,
                Handler = (args, done) =>
                {
                    SimulationHost host = registry.Context?.Host;
                    if (host == null || !host.IsReady)
                    {
                        done?.Invoke("Simulation is not ready.");
                        return;
                    }

                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    Vector2Int cell = PolarCellMapping.FromNormalized(host.Grid,
                        AiToolRegistry.ArgFloat(parsed, "theta01"), AiToolRegistry.ArgFloat(parsed, "radius01"));
                    int brush = Mathf.Clamp(AiToolRegistry.ArgInt(parsed, "brush_radius", 8), 0, 128);
                    string materialName = AiToolRegistry.ArgString(parsed, "material");
                    string channelName = AiToolRegistry.ArgString(parsed, "channel");
                    string error = null;
                    if (!string.IsNullOrWhiteSpace(materialName) && TryParseMaterial(materialName, out uint materialId))
                    {
                        host.QueueMaterialPaint(cell, brush, materialId);
                        done?.Invoke($"Painted {materialName} at ({cell.x},{cell.y}) r={brush}.");
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(channelName)
                        && TryParseFieldChannel(channelName, AiToolRegistry.ArgFloat(parsed, "amount", 1f), out int channel, out float amount, out error))
                    {
                        host.QueueFieldDeposit(cell, Mathf.Max(1, brush), channel, amount);
                        done?.Invoke($"Deposited channel {channel} amount {amount:0.###} at ({cell.x},{cell.y}) r={brush}.");
                        return;
                    }

                    done?.Invoke(string.IsNullOrWhiteSpace(channelName)
                        ? "Provide material or channel."
                        : error);
                }
            });
            registry.Add(new AiTool
            {
                Name = "seed_life",
                Description = "Seed flora, cricket, wasp, grass, or tree at a polar cell.",
                Parameters = AiToolRegistry.ObjectSchema(
                    ("species", AiToolRegistry.StringProp("Species to seed.", "flora", "cricket", "wasp", "grass", "tree"), true),
                    ("theta01", AiToolRegistry.NumberProp("Angular position 0..1."), true),
                    ("radius01", AiToolRegistry.NumberProp("Radial position 0..1."), true),
                    ("brush_radius", AiToolRegistry.IntegerProp("Seed radius in cells."), false)),
                AllowedSteps = ActStepMask.Convert,
                RequiresDeity = true,
                Handler = (args, done) =>
                {
                    SimulationHost host = registry.Context?.Host;
                    if (host == null || !host.IsReady)
                    {
                        done?.Invoke("Simulation is not ready.");
                        return;
                    }

                    JObject parsed = AiToolRegistry.ParseArgs(args);
                    Vector2Int cell = PolarCellMapping.FromNormalized(host.Grid,
                        AiToolRegistry.ArgFloat(parsed, "theta01"), AiToolRegistry.ArgFloat(parsed, "radius01"));
                    int brush = Mathf.Max(0, AiToolRegistry.ArgInt(parsed, "brush_radius", 2));
                    string species = AiToolRegistry.ArgString(parsed, "species").ToLowerInvariant();
                    switch (species)
                    {
                        case "flora":
                        case "algae":
                            host.QueueFloraSeed(cell, brush, 0.35f);
                            break;
                        case "cricket":
                        case "fauna":
                            host.QueueFaunaSeed(cell, brush, MaterialIds.CricketEgg);
                            break;
                        case "wasp":
                            host.QueueWaspSeed(cell, brush, MaterialIds.WaspEgg);
                            break;
                        case "grass":
                            host.QueueGrassSeed(cell, brush);
                            break;
                        case "tree":
                            host.QueueTreeSprout(cell, brush);
                            break;
                        default:
                            done?.Invoke("Unknown species. Use flora, cricket, wasp, grass, or tree.");
                            return;
                    }

                    done?.Invoke($"Queued {species} seed at ({cell.x},{cell.y}) r={brush}.");
                }
            });
        }

        private static System.Collections.IEnumerator ReleaseProbeAfter(ProbeController probe, ProbeAction action, float duration, Action<string> done)
        {
            yield return new WaitForSecondsRealtime(duration);
            if (probe != null && probe.ActiveAction == action)
                probe.SetAction(ProbeAction.None);
            done?.Invoke($"Held {action} for {duration.ToString("0.##", CultureInfo.InvariantCulture)}s.");
        }

        private static bool TryParseProbeAction(string name, out ProbeAction action)
        {
            action = ProbeAction.None;
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "humidity":
                case "vapor":
                    action = ProbeAction.Humidity;
                    return true;
                case "water":
                    action = ProbeAction.Water;
                    return true;
                case "soil":
                    action = ProbeAction.Soil;
                    return true;
                case "cool":
                    action = ProbeAction.Cool;
                    return true;
                case "heat":
                    action = ProbeAction.Heat;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseFieldChannel(string name, float amount, out int channel, out float applied, out string error)
        {
            channel = 0;
            applied = amount;
            error = null;
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "heat":
                    channel = 1;
                    applied = amount;
                    return true;
                case "cool":
                    channel = 1;
                    applied = -Mathf.Abs(amount);
                    return true;
                case "water":
                    channel = 2;
                    return true;
                case "pressure":
                    channel = 3;
                    return true;
                case "nutrients":
                    channel = 4;
                    return true;
                case "groundwater":
                    channel = 5;
                    return true;
                case "humidity":
                case "vapor":
                    channel = 6;
                    return true;
                default:
                    error = "Unknown channel. Use heat, cool, water, humidity, nutrients, groundwater, or pressure.";
                    return false;
            }
        }

        private static bool TryParseMaterial(string name, out uint materialId)
        {
            materialId = MaterialIds.Soil;
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "soil": materialId = MaterialIds.Soil; return true;
                case "water": materialId = MaterialIds.Water; return true;
                case "sediment": materialId = MaterialIds.Sediment; return true;
                case "ice": materialId = MaterialIds.Ice; return true;
                case "ash": materialId = MaterialIds.Ash; return true;
                case "air": materialId = MaterialIds.Air; return true;
                case "clay": materialId = MaterialIds.Clay; return true;
                default: return false;
            }
        }

        private static string FormatPlanet(WorldWaterMetrics metrics)
        {
            return
                $"meanT={metrics.MeanTemperature:0.##} meanRH={metrics.MeanRelativeHumidity:0.###} moisture={metrics.MeanMoisture:0.###} " +
                $"pressure={metrics.MeanPressure:0.###} cloud={metrics.CloudCover:0.###} " +
                $"surfaceWater={metrics.SurfaceWaterMass:0.##} groundwater={metrics.GroundwaterMass:0.##} vapor={metrics.VaporMass:0.##} " +
                $"organisms={metrics.OrganismCount} fireCells={metrics.BurningCellCount} wind={metrics.MeanWindSpeed:0.###}";
        }

        private static string FormatInspection(CellInspection inspection)
        {
            return
                $"cell=({inspection.cell.x},{inspection.cell.y}) mat={inspection.materialId} " +
                $"T={inspection.state.x:0.##} P={inspection.state.y:0.###} water={inspection.state.z:0.###} " +
                $"vapor={inspection.aux.x:0.###} groundwater={inspection.aux.y:0.###} nutrients={inspection.aux.z:0.###} " +
                $"light={inspection.light:0.###} biomass={inspection.life.y:0.###} " +
                $"sedimentRho={inspection.mobileSediment:0.###} structural={inspection.mobileStructural:0.###}";
        }
    }
}
