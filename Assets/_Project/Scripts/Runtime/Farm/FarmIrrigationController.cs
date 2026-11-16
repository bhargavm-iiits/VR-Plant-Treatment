using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Farm
{
    public enum IrrigationState
    {
        /// <summary>Dry: no water in the pipes, no pools.</summary>
        Off,
        /// <summary>Water is travelling out along the pipes and the pools are filling.</summary>
        Flowing,
        /// <summary>Every pipe is full and every row's pool has reached its standing level.</summary>
        Standing,
        /// <summary>Switched off: the flow stops and the pools sink and dry.</summary>
        Draining,
    }

    /// <summary>
    /// The Farm irrigation: one button turns it on (water runs out along the main line, then each row's branch,
    /// drips at every plant, then shallow standing water fills each row) and off again (the flow stops and the
    /// pools drain). The pipes and pools are a few merged meshes (FarmIrrigationBuilder); their shaders read the
    /// water front from shader globals, so one float animates every pipe and every row's pool:
    ///   pipe water shows where the network distance from the tank is below <see cref="Front"/>;
    ///   a row's pool fill = saturate((Front - row arrival - pool delay) / fill span) * <see cref="Level"/>;
    ///   wet soil spreads from each emitter once the front passes it and dries slowly after switching off.
    /// <see cref="Step"/> advances the sequence by a time step (Update calls it with Time.deltaTime), so it is
    /// frame-rate independent and testable without Play mode.
    /// </summary>
    public sealed class FarmIrrigationController : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            /// <summary>plantId_diseaseId, as the Farm row's name.</summary>
            public string id;
            /// <summary>Network distance (m) from the tank to where the row's branch pipe reaches its bed.</summary>
            public float arrival;
            /// <summary>Length (m) of the row's branch pipe.</summary>
            public float branchLength;
            /// <summary>World centre of the row's pool at its standing level, and its size (X, Z) in metres.</summary>
            public Vector3 poolCentre;
            public Vector2 poolSize;
        }

        [Serializable]
        public struct DripPoint
        {
            public Vector3 position;
            /// <summary>Initial drop velocity (drips) or main spray direction (sprinklers).</summary>
            public Vector3 velocity;
            /// <summary>Network distance (m) from the tank: the point drips once the water front has passed it.</summary>
            public float distance;
            /// <summary>True for a tree row's micro-sprinkler (a fan of spray) rather than a drip emitter.</summary>
            public bool spray;
        }

        public static readonly int FrontId = Shader.PropertyToID("_BTP_IrrigationFront");
        public static readonly int FlowId = Shader.PropertyToID("_BTP_IrrigationFlow");
        public static readonly int LevelId = Shader.PropertyToID("_BTP_IrrigationLevel");
        public static readonly int FillSpanId = Shader.PropertyToID("_BTP_IrrigationFillSpan");
        public static readonly int WetFrontId = Shader.PropertyToID("_BTP_IrrigationWetFront");
        public static readonly int PoolDelayId = Shader.PropertyToID("_BTP_IrrigationPoolDelay");
        public static readonly int WetnessId = Shader.PropertyToID("_BTP_IrrigationWetness");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Button toggleButton;
        [SerializeField] TMP_Text buttonLabel;
        [SerializeField] TMP_Text statusText;
        [SerializeField] Graphic statusLamp;
        [SerializeField] ParticleSystem drips;
        [SerializeField] List<Renderer> pipeRenderers = new List<Renderer>();
        [SerializeField] List<Renderer> poolRenderers = new List<Renderer>();
        [SerializeField] List<Row> rows = new List<Row>();
        [SerializeField] List<DripPoint> dripPoints = new List<DripPoint>();
        [Tooltip("Total length of the pipe network from the tank to the farthest pipe end, in metres.")]
        [SerializeField] float networkLength = 1f;

        [Header("Timing")]
        [Tooltip("Speed of the water front along the pipes, m/s.")]
        [SerializeField] float flowSpeed = 8f;
        [Tooltip("Seconds a row's pool takes to rise to its standing level once water reaches the row.")]
        [SerializeField] float fillSeconds = 7f;
        [Tooltip("Seconds after the water reaches a row before standing water appears (the soil wets first).")]
        [SerializeField] float poolDelaySeconds = 4f;
        [SerializeField] float flowFadeInSeconds = 0.6f;
        [SerializeField] float flowFadeOutSeconds = 1.5f;
        [Tooltip("Seconds the pools take to drain and dry after switching off.")]
        [SerializeField] float drainSeconds = 5f;

        [Tooltip("Seconds the wet soil takes to dry back after switching off.")]
        [SerializeField] float drySeconds = 20f;

        [Header("Drips")]
        [SerializeField] float dripsPerSecond = 1.6f;
        [SerializeField] float spraysPerSecond = 22f;

        [Header("Hardware (optional)")]
        [Tooltip("Ball-valve lever: parallel to the pipe (yaw 0) is open, across it (yaw 90) is closed.")]
        [SerializeField] Transform valveHandle;
        [Tooltip("Pressure-gauge needle, turned about its local Z.")]
        [SerializeField] Transform gaugeNeedle;
        [Tooltip("The 3D push-button cap; it moves 6 mm along its local +Z (into the box) while pressed.")]
        [SerializeField] Transform pushButton;
        [SerializeField] Renderer runLamp;
        [Tooltip("Fine spray of the tree micro-sprinklers (drips use the drip system).")]
        [SerializeField] ParticleSystem mist;
        [SerializeField] float gaugeZeroAngle = -135f;
        [SerializeField] float gaugeFullAngle = 135f;
        [Tooltip("Gauge reading while running, as a fraction of full scale (about 2 bar on a 4 bar gauge).")]
        [SerializeField] float workingPressure = 0.5f;

        float[] dripAccumulators;
        float valveOpen;
        float pressure;
        float pressTimer;
        Vector3 pushButtonRest;
        bool pushButtonRestSet;
        MaterialPropertyBlock lampBlock;

        public bool IsOn { get; private set; }
        public IrrigationState State { get; private set; } = IrrigationState.Off;
        /// <summary>Network distance (m) the water front has reached; it keeps rising past the pipes while the pools fill.</summary>
        public float Front { get; private set; }
        /// <summary>0..1: water running in the pipes (and drips).</summary>
        public float Flow { get; private set; }
        /// <summary>0..1: multiplier on every pool's fill (1 while on, falls to 0 while draining).</summary>
        public float Level { get; private set; }
        /// <summary>0..1: wet soil around the emitters (dries slowly after switching off).</summary>
        public float Wetness { get; private set; }
        /// <summary>Water front for the wet soil: follows <see cref="Front"/> while on, held while the soil dries.</summary>
        public float WetFront { get; private set; }

        public float FillSpan => Mathf.Max(0.01f, flowSpeed * fillSeconds);
        /// <summary>Front travel (m) between the water reaching a row and its pool starting to rise.</summary>
        public float PoolDelay => Mathf.Max(0f, flowSpeed * poolDelaySeconds);
        /// <summary>Front value at which the pipes are full and the last row's pool has finished filling.</summary>
        public float FrontMax
        {
            get
            {
                var last = 0f;
                foreach (var row in rows)
                    last = Mathf.Max(last, row.arrival);
                return Mathf.Max(networkLength, last + PoolDelay + FillSpan);
            }
        }

        public IReadOnlyList<Row> Rows => rows;
        public IReadOnlyList<DripPoint> DripPoints => dripPoints;
        public IReadOnlyList<Renderer> PipeRenderers => pipeRenderers;
        public IReadOnlyList<Renderer> PoolRenderers => poolRenderers;
        public Button ToggleButton => toggleButton;
        public ParticleSystem Drips => drips;
        public float NetworkLength => networkLength;

        public event Action<IrrigationState> StateChanged;

        internal void Bind(Button button, TMP_Text label, TMP_Text status, Graphic lamp, ParticleSystem dripSystem,
            List<Renderer> pipes, List<Renderer> pools, List<Row> rowData, List<DripPoint> points, float length)
        {
            toggleButton = button;
            buttonLabel = label;
            statusText = status;
            statusLamp = lamp;
            drips = dripSystem;
            pipeRenderers = pipes;
            poolRenderers = pools;
            rows = rowData;
            dripPoints = points;
            networkLength = Mathf.Max(1f, length);
        }

        internal void BindHardware(Transform valve, Transform needle, Transform button, Renderer lamp, ParticleSystem mistSystem)
        {
            mist = mistSystem;
            valveHandle = valve;
            gaugeNeedle = needle;
            pushButton = button;
            runLamp = lamp;
        }

        public Transform ValveHandle => valveHandle;
        public Transform GaugeNeedle => gaugeNeedle;
        public Transform PushButton => pushButton;

        /// <summary>Test hook: rows and timings without any scene objects.</summary>
        internal void Configure(List<Row> rowData, float length, float speed, float fill, float drain)
        {
            rows = rowData;
            networkLength = Mathf.Max(1f, length);
            flowSpeed = speed;
            fillSeconds = fill;
            drainSeconds = drain;
        }

        void Awake()
        {
            if (toggleButton != null)
                toggleButton.onClick.AddListener(Toggle);
        }

        void OnEnable()
        {
            ApplyGlobals();
            RefreshUI();
        }

        void OnDisable()
        {
            // Leave the editor (and the next scene) dry.
            Shader.SetGlobalFloat(FrontId, 0f);
            Shader.SetGlobalFloat(FlowId, 0f);
            Shader.SetGlobalFloat(LevelId, 0f);
            Shader.SetGlobalFloat(WetFrontId, 0f);
            Shader.SetGlobalFloat(WetnessId, 0f);
        }

        void OnDestroy()
        {
            if (toggleButton != null)
                toggleButton.onClick.RemoveListener(Toggle);
        }

        void Update() => Step(Time.deltaTime);

        /// <summary>The one button: on (water flows, then stands) or off (flow stops, pools drain).</summary>
        public void Toggle()
        {
            pressTimer = 0.18f;
            SetOn(!IsOn);
        }

        public void SetOn(bool on)
        {
            if (on == IsOn)
                return;
            IsOn = on;
            Step(0f);
        }

        /// <summary>Back to dry and off immediately.</summary>
        public void ResetToOff()
        {
            IsOn = false;
            Front = Flow = Level = Wetness = WetFront = 0f;
            valveOpen = pressure = 0f;
            SetState(IrrigationState.Off);
            ApplyGlobals();
            RefreshUI();
        }

        /// <summary>Advances the sequence by <paramref name="deltaTime"/> seconds.</summary>
        public void Step(float deltaTime)
        {
            var dt = Mathf.Max(0f, deltaTime);
            IrrigationState next;
            if (IsOn)
            {
                Flow = Mathf.MoveTowards(Flow, 1f, dt / Mathf.Max(0.01f, flowFadeInSeconds));
                Level = Mathf.MoveTowards(Level, 1f, dt / Mathf.Max(0.01f, flowFadeInSeconds * 2f));
                var max = FrontMax;
                Front = Mathf.Min(Front + flowSpeed * dt, max);
                next = Front >= max ? IrrigationState.Standing : IrrigationState.Flowing;
            }
            else
            {
                Flow = Mathf.MoveTowards(Flow, 0f, dt / Mathf.Max(0.01f, flowFadeOutSeconds));
                Level = Mathf.MoveTowards(Level, 0f, dt / Mathf.Max(0.01f, drainSeconds));
                if (Flow <= 0f && Level <= 0f)
                {
                    Front = 0f;
                    next = IrrigationState.Off;
                }
                else
                {
                    next = IrrigationState.Draining;
                }
            }

            // Wet soil: follows the front while on; dries slowly (and keeps its extent) after switching off.
            if (IsOn)
            {
                WetFront = Mathf.Max(WetFront, Front);
                Wetness = Mathf.MoveTowards(Wetness, 1f, dt / 0.5f);
            }
            else
            {
                Wetness = Mathf.MoveTowards(Wetness, 0f, dt / Mathf.Max(0.01f, drySeconds));
                if (Wetness <= 0f)
                    WetFront = 0f;
            }

            SetState(next);
            ApplyGlobals();
            AnimateHardware(dt);
            if (Application.isPlaying)
                EmitDrips(dt);
            RefreshUI();
        }

        /// <summary>0..1 fill of row <paramref name="index"/>'s pool (what its shader shows).</summary>
        public float RowFill(int index)
        {
            if (index < 0 || index >= rows.Count)
                return 0f;
            return Mathf.Clamp01((Front - rows[index].arrival - PoolDelay) / FillSpan) * Level;
        }

        /// <summary>0..1: how far the water has spread through the pipes.</summary>
        public float PipeFill => Mathf.Clamp01(Front / networkLength) * Flow;

        void SetState(IrrigationState state)
        {
            if (state == State)
                return;
            State = state;
            StateChanged?.Invoke(state);
        }

        void ApplyGlobals()
        {
            Shader.SetGlobalFloat(FrontId, Front);
            Shader.SetGlobalFloat(FlowId, Flow);
            Shader.SetGlobalFloat(LevelId, Level);
            Shader.SetGlobalFloat(FillSpanId, FillSpan);
            Shader.SetGlobalFloat(PoolDelayId, PoolDelay);
            Shader.SetGlobalFloat(WetFrontId, WetFront);
            Shader.SetGlobalFloat(WetnessId, Wetness);
        }

        /// <summary>Valve lever, gauge needle, the push button's travel and the run lamp.</summary>
        void AnimateHardware(float dt)
        {
            // The lever turns a quarter turn in about half a second; pressure follows the open valve.
            valveOpen = Mathf.MoveTowards(valveOpen, IsOn ? 1f : 0f, dt / 0.5f);
            var target = Flow * valveOpen * workingPressure;
            if (target > 0.01f)
                target += Mathf.Sin(Time.time * 6.3f) * 0.006f + Mathf.Sin(Time.time * 2.1f) * 0.004f;
            pressure = Mathf.Lerp(pressure, target, 1f - Mathf.Exp(-dt * 4f));
            if (valveHandle != null)
                valveHandle.localRotation = Quaternion.Euler(0f, Mathf.Lerp(90f, 0f, valveOpen), 0f);
            if (gaugeNeedle != null)
                gaugeNeedle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(gaugeZeroAngle, gaugeFullAngle, Mathf.Clamp01(pressure)));

            if (pushButton != null)
            {
                if (!pushButtonRestSet)
                {
                    pushButtonRest = pushButton.localPosition;
                    pushButtonRestSet = true;
                }

                pressTimer = Mathf.Max(0f, pressTimer - dt);
                pushButton.localPosition = pushButtonRest + Vector3.forward * (pressTimer > 0f ? 0.006f : 0f);
            }

            if (runLamp != null)
            {
                lampBlock ??= new MaterialPropertyBlock();
                Color lamp = State switch
                {
                    IrrigationState.Flowing or IrrigationState.Standing => new Color(0.25f, 1f, 0.3f, 1f),
                    IrrigationState.Draining => new Color(1f, 0.62f, 0.12f, 1f),
                    _ => new Color(0.06f, 0.10f, 0.06f, 1f),
                };
                lampBlock.SetColor(BaseColorId, lamp);
                runLamp.SetPropertyBlock(lampBlock);
            }
        }

        static readonly Color LampOff = new Color(0.35f, 0.38f, 0.42f, 1f);
        static readonly Color LampWater = new Color(0.25f, 0.65f, 1f, 1f);
        static readonly Color LampDrain = new Color(0.98f, 0.75f, 0.14f, 1f);

        string lastStatus;

        void RefreshUI()
        {
            string status;
            Color lamp;
            switch (State)
            {
                case IrrigationState.Flowing:
                    status = $"FLOWING {Mathf.RoundToInt(Front / FrontMax * 100f)}%";
                    lamp = LampWater;
                    break;
                case IrrigationState.Standing:
                    status = "STANDING WATER";
                    lamp = LampWater;
                    break;
                case IrrigationState.Draining:
                    status = "DRAINING";
                    lamp = LampDrain;
                    break;
                default:
                    status = "OFF";
                    lamp = LampOff;
                    break;
            }

            if (status != lastStatus)
            {
                lastStatus = status;
                if (statusText != null)
                    statusText.text = status;
                if (buttonLabel != null)
                    buttonLabel.text = IsOn ? "PRESS TO STOP" : "PRESS TO START";
                if (statusLamp != null)
                    statusLamp.color = lamp;
            }
        }

        void EmitDrips(float dt)
        {
            if (drips == null || dt <= 0f || Flow <= 0.05f)
                return;
            if (dripAccumulators == null || dripAccumulators.Length != dripPoints.Count)
                dripAccumulators = new float[dripPoints.Count];

            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (var i = 0; i < dripPoints.Count; i++)
            {
                var point = dripPoints[i];
                if (Front < point.distance)
                    continue;
                dripAccumulators[i] += (point.spray ? spraysPerSecond : dripsPerSecond) * Flow * dt;
                while (dripAccumulators[i] >= 1f)
                {
                    dripAccumulators[i] -= 1f;
                    if (point.spray)
                    {
                        // Micro-sprinkler: a fine, low, misty fan.
                        var angle = UnityEngine.Random.value * Mathf.PI * 2f;
                        var outward = UnityEngine.Random.Range(0.5f, 1.0f);
                        emit.position = point.position;
                        emit.velocity = new Vector3(Mathf.Cos(angle) * outward, UnityEngine.Random.Range(0.6f, 1.0f), Mathf.Sin(angle) * outward);
                        emit.startLifetime = UnityEngine.Random.Range(0.45f, 0.7f);
                        emit.startSize = UnityEngine.Random.Range(0.008f, 0.014f);
                        emit.startColor = new Color(0.9f, 0.93f, 0.95f, 0.35f);
                    }
                    else
                    {
                        // A drop forming at the emitter and falling to the soil.
                        emit.position = point.position + new Vector3(UnityEngine.Random.Range(-0.004f, 0.004f), 0f, UnityEngine.Random.Range(-0.006f, 0.006f));
                        emit.velocity = point.velocity;
                        emit.startLifetime = UnityEngine.Random.Range(0.5f, 0.7f);
                        emit.startSize = UnityEngine.Random.Range(0.007f, 0.01f);
                        emit.startColor = new Color(0.85f, 0.88f, 0.9f, 0.55f);
                    }

                    (point.spray && mist != null ? mist : drips).Emit(emit, 1);
                }
            }
        }
    }
}
