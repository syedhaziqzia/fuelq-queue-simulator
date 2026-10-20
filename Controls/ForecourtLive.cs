using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FuelQ.Controls;

/// <summary>
/// A small live queue simulation drawn in real time: vehicles arrive at random,
/// join the queue, drive to the first free pump, fuel up and leave.
/// It shows the idea behind every page of the app at a glance.
/// Runs only while visible.
/// </summary>
public class ForecourtLive : FrameworkElement
{
    private enum Phase { Queued, ToPump, Fueling, Leaving }

    private sealed class Vehicle
    {
        public double X, Y;
        public bool IsBike;
        public Phase Phase;
        public int Pump = -1;
        public double ServiceTotal, ServiceLeft;
    }

    private const int Pumps = 3;
    private const int MaxQueue = 11;          // drivers balk beyond this (road spill-back)
    private const double MeanGap = 1.15;      // seconds between arrivals
    private const double BikeService = 2.1;   // mean seconds at the pump
    private const double CarService = 4.3;

    private readonly List<Vehicle> _vehicles = new();
    private readonly List<Vehicle> _queue = new();
    private readonly Vehicle?[] _atPump = new Vehicle?[Pumps];
    private readonly Random _rng = new(2026);
    private readonly Stopwatch _clock = new();

    private double _nextArrival = 0.3;
    private double _laneOffset;
    private double _lastTime;
    private int _served;
    private bool _running, _warmed;

    // Palette ("forecourt at night")
    private static readonly Brush Asphalt   = Frozen(Color.FromRgb(0x14, 0x12, 0x10));
    private static readonly Brush Island    = Frozen(Color.FromRgb(0x26, 0x22, 0x1B));
    private static readonly Brush Amber     = Frozen(Color.FromRgb(0xFF, 0xB0, 0x20));
    private static readonly Brush AmberDim  = Frozen(Color.FromArgb(0x55, 0xFF, 0xB0, 0x20));
    private static readonly Brush Teal      = Frozen(Color.FromRgb(0x3E, 0xCF, 0xB2));
    private static readonly Brush Glass     = Frozen(Color.FromArgb(0xAA, 0x14, 0x12, 0x10));
    private static readonly Brush Idle      = Frozen(Color.FromRgb(0x6B, 0xD6, 0x8F));
    private static readonly Brush Label     = Frozen(Color.FromRgb(0xB3, 0xAA, 0x9A));
    private static readonly Brush Strong    = Frozen(Color.FromRgb(0xF4, 0xEF, 0xE6));
    private static readonly Pen   Marking   = FrozenPen(Color.FromArgb(0x70, 0xF4, 0xEF, 0xE6), 2);
    private static readonly Pen   Route     = FrozenPen(Color.FromArgb(0x30, 0xFF, 0xB0, 0x20), 1.2, dashed: true);
    private static readonly Pen   PumpEdge  = FrozenPen(Color.FromRgb(0x3A, 0x35, 0x2C), 1);
    private static readonly Pen   PumpBusy  = FrozenPen(Color.FromRgb(0xFF, 0xB0, 0x20), 1.5);
    private static readonly Typeface Display = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public ForecourtLive()
    {
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Start(); else Stop(); };
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        if (_running || !IsVisible) return;
        _running = true;
        _clock.Restart();
        _lastTime = 0;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (ActualWidth < 50) return;

        // Fill the scene before the first frame so it never starts empty
        if (!_warmed)
        {
            for (int i = 0; i < 200; i++) Step(0.05);
            _warmed = true;
        }

        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.05, now - _lastTime);   // clamp after pauses
        _lastTime = now;
        Step(dt);
        InvalidateVisual();
    }

    // ── Layout helpers ────────────────────────────────────────────

    private double W => ActualWidth;
    private double H => ActualHeight;
    private double LaneY => H * 0.52;
    private double QueueHeadX => W * 0.50;
    private double PumpX => W * 0.70;
    private double PumpY(int p) => 46 + p * (H - 92) / (Pumps - 1);
    private double SlotX(int k) => QueueHeadX - k * 32;

    // ── Simulation step ───────────────────────────────────────────

    private void Step(double dt)
    {
        _laneOffset = (_laneOffset + dt * 26) % 24;

        // Arrivals (Poisson process); drivers balk when the queue reaches the road
        _nextArrival -= dt;
        if (_nextArrival <= 0)
        {
            _nextArrival = Exp(MeanGap);
            if (_queue.Count < MaxQueue)
            {
                var v = new Vehicle { X = -40, Y = LaneY, IsBike = _rng.NextDouble() < 0.7, Phase = Phase.Queued };
                _vehicles.Add(v);
                _queue.Add(v);
            }
        }

        // Head of the queue takes the first free pump
        for (int p = 0; p < Pumps && _queue.Count > 0; p++)
        {
            if (_atPump[p] != null) continue;
            var v = _queue[0];
            _queue.RemoveAt(0);
            v.Phase = Phase.ToPump;
            v.Pump = p;
            v.ServiceTotal = v.IsBike ? Math.Clamp(Exp(BikeService), 0.8, 5) : Math.Clamp(Exp(CarService), 1.5, 9);
            v.ServiceLeft = v.ServiceTotal;
            _atPump[p] = v;
        }

        for (int i = _vehicles.Count - 1; i >= 0; i--)
        {
            var v = _vehicles[i];
            double tx, ty, speed = 5;
            switch (v.Phase)
            {
                case Phase.Queued:
                    int k = _queue.IndexOf(v);
                    tx = SlotX(k); ty = LaneY;
                    break;
                case Phase.ToPump:
                    tx = PumpX - 34; ty = PumpY(v.Pump);
                    if (Math.Abs(v.X - tx) < 1.5 && Math.Abs(v.Y - ty) < 1.5) v.Phase = Phase.Fueling;
                    break;
                case Phase.Fueling:
                    tx = PumpX - 34; ty = PumpY(v.Pump);
                    v.ServiceLeft -= dt;
                    if (v.ServiceLeft <= 0)
                    {
                        v.Phase = Phase.Leaving;
                        _atPump[v.Pump] = null;
                        _served++;
                    }
                    break;
                default: // Leaving
                    tx = W + 80; ty = PumpY(v.Pump); speed = 1.6;
                    if (v.X > W + 40) { _vehicles.RemoveAt(i); continue; }
                    break;
            }
            // Ease toward the target (frame-rate independent)
            double a = 1 - Math.Exp(-dt * speed);
            v.X += (tx - v.X) * a;
            v.Y += (ty - v.Y) * a;
        }
    }

    private double Exp(double mean) => -mean * Math.Log(1 - _rng.NextDouble());

    // ── Drawing ───────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double w = W, h = H;
        if (w < 50 || h < 50) return;

        dc.DrawRoundedRectangle(Asphalt, null, new Rect(0, 0, w, h), 12, 12);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), 12, 12));

        // Moving lane markings along the approach road
        for (double x = -24 + _laneOffset; x < QueueHeadX + 20; x += 24)
            dc.DrawLine(Marking, new Point(x, LaneY + 18), new Point(x + 11, LaneY + 18));

        // Routes from the queue head to each pump
        for (int p = 0; p < Pumps; p++)
            dc.DrawLine(Route, new Point(QueueHeadX + 14, LaneY), new Point(PumpX - 34, PumpY(p)));

        // Pump island
        dc.DrawRoundedRectangle(Island, null, new Rect(PumpX - 8, 22, 40, h - 44), 8, 8);
        int busy = 0;
        for (int p = 0; p < Pumps; p++)
        {
            var occupant = _atPump[p];
            bool fueling = occupant is { Phase: Phase.Fueling };
            if (occupant != null) busy++;
            double y = PumpY(p);
            var body = new Rect(PumpX, y - 16, 24, 32);
            dc.DrawRoundedRectangle(Asphalt, fueling ? PumpBusy : PumpEdge, body, 4, 4);
            DrawText(dc, $"P{p + 1}", PumpX + 12, y - 8, 10, fueling ? Amber : Label, center: true);
            dc.DrawEllipse(occupant == null ? Idle : Amber, null, new Point(PumpX + 12, y + 9), 3, 3);

            // Service progress under the pump
            if (fueling && occupant!.ServiceTotal > 0)
            {
                double done = 1 - occupant.ServiceLeft / occupant.ServiceTotal;
                dc.DrawRoundedRectangle(AmberDim, null, new Rect(PumpX - 2, y + 19, 28, 3), 1.5, 1.5);
                dc.DrawRoundedRectangle(Amber, null, new Rect(PumpX - 2, y + 19, 28 * done, 3), 1.5, 1.5);
            }
        }

        foreach (var v in _vehicles) DrawVehicle(dc, v);

        // Labels
        DrawText(dc, "ARRIVALS →", 14, 12, 10.5, Label);
        DrawText(dc, $"QUEUE  {_queue.Count}", QueueHeadX - 70, LaneY - 34, 11, _queue.Count > 6 ? Amber : Strong);
        DrawText(dc, $"PUMPS  {busy}/{Pumps} BUSY", PumpX + 12, 6, 10.5, Label, center: true);
        DrawText(dc, $"SERVED  {_served}", w - 14, 12, 10.5, Label, right: true);

        // Legend
        dc.DrawRoundedRectangle(Amber, null, new Rect(14, h - 20, 14, 8), 2.5, 2.5);
        DrawText(dc, "CAR", 32, h - 23, 10, Label);
        dc.DrawRoundedRectangle(Teal, null, new Rect(66, h - 19, 10, 6), 2, 2);
        DrawText(dc, "BIKE", 80, h - 23, 10, Label);

        dc.Pop();
    }

    private static void DrawVehicle(DrawingContext dc, Vehicle v)
    {
        if (v.IsBike)
        {
            dc.DrawRoundedRectangle(Teal, null, new Rect(v.X - 9, v.Y - 4.5, 18, 9), 4, 4);
            dc.DrawEllipse(Glass, null, new Point(v.X + 3, v.Y), 2, 2);
        }
        else
        {
            dc.DrawRoundedRectangle(Amber, null, new Rect(v.X - 15, v.Y - 8, 30, 16), 5, 5);
            dc.DrawRoundedRectangle(Glass, null, new Rect(v.X + 2, v.Y - 5.5, 8, 11), 2.5, 2.5);
        }
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush,
        bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Display, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (center) x -= ft.Width / 2;
        else if (right) x -= ft.Width;
        dc.DrawText(ft, new Point(x, y));
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double thickness, bool dashed = false)
    {
        var p = new Pen(Frozen(c), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (dashed) p.DashStyle = new DashStyle([3, 3], 0);
        p.Freeze();
        return p;
    }
}
