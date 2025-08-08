﻿using System;
using System.Collections.Generic;
using Cronos;
using JetBrains.Annotations;

namespace BloodRain {
  public class BloodRainSchedule {
    public CronExpression CronExpression;
    public float DurationIrlMinutes;
    public DateTime NextStartTime;
    public TimeSpan? NextWarning;

    public BloodRainSchedule(CronExpression cronExpression, float durationIrlMinutes, DateTime nextStartTime,
      TimeSpan? nextWarning = null) {
      CronExpression = cronExpression;
      DurationIrlMinutes = durationIrlMinutes;
      NextStartTime = nextStartTime;
      NextWarning = nextWarning;
    }
  }

  public class BloodRain {
    public const float DefaultDurationIrlMinutes = 15f;
    public const float DefaultWarningIrlMinutes = 15f;
    public const string BloodRainBuff = "buff_blood_rain_strong";
    public const string BloodRainRemainingSecondsCVar = "blood_rain_remaining_seconds";

    [CanBeNull] private static BloodRainSchedule _schedule;
    private static int _minGameDay = 7;

    private static DateTime? _endTime;

    public static bool IsBloodRainTime() {
      return _endTime is not null;
    }

    public static float GetRemainingBloodRainSeconds() {
      if (_endTime is null) {
        return -1;
      }

      return (float)((DateTime)_endTime - DateTime.Now).TotalSeconds;
    }

    public static void Update() {
      DateTime now = DateTime.Now;
      if (_schedule?.NextWarning is not null && now >= _schedule.NextStartTime - _schedule.NextWarning) {
        WarnPlayers();
      }

      if (_schedule is not null && now >= _schedule.NextStartTime) {
        var currentDay = GameUtils.WorldTimeToDays(GameManager.Instance.World.worldTime);
        if (currentDay >= _minGameDay) {
          StartBloodRain(_schedule.DurationIrlMinutes);
        }

        UpdateNextStartTime();
        return;
      }

      if (_endTime is null || now < _endTime) {
        UpdateBloodRainBuff();
        return;
      }

      StopBloodRain();
    }

    public static void UpdateNextStartTime() {
      if (_schedule is null) {
        return;
      }

      DateTime? next = GetNextStartTime(_schedule.CronExpression);
      if (next is null) {
        SetSchedule(null);
        return;
      }

      _schedule.NextStartTime = (DateTime)next;
      _schedule.NextWarning = GetNextWarningTimeSpan((DateTime)next);
    }

    public static void StartBloodRain(float durationIrlMinutes = DefaultDurationIrlMinutes) {
      if (_endTime is null) {
        GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1,
          "[ff0000]The blood rain is upon us; defend yourselves![-]", null,
          EMessageSender.None);
      }

      DateTime newEndTime = DateTime.Now + TimeSpan.FromMinutes(durationIrlMinutes);
      if (_endTime is null || _endTime < newEndTime) {
        _endTime = newEndTime;
        UpdateBloodRainBuff();
      }
    }

    public static void StopBloodRain() {
      _endTime = null;
      UpdateBloodRainBuff();
    }

    private static void UpdateBloodRainBuff() {
      List<EntityPlayer> playerList = GameManager.Instance?.World?.Players?.list;
      if (playerList is null) {
        return;
      }

      var remaining = GetRemainingBloodRainSeconds();
      foreach (EntityPlayer p in playerList) {
        if (IsBloodRainTime()) {
          p.SetCVar(BloodRainRemainingSecondsCVar, remaining);
          if (!p.Buffs.HasBuff(BloodRainBuff)) {
            p.Buffs.AddBuff(BloodRainBuff);
          }
        } else {
          p.Buffs.RemoveCustomVar(BloodRainRemainingSecondsCVar);
          if (p.Buffs.HasBuff(BloodRainBuff)) {
            p.Buffs.RemoveBuff(BloodRainBuff);
          }
        }
      }
    }

    private static void WarnPlayers() {
      if (_schedule?.NextWarning is null) {
        return;
      }

      GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1,
        $"[ff0000]The blood rain will start in {((TimeSpan)_schedule.NextWarning).TotalMinutes} minute(s).[-]",
        null, EMessageSender.None);
      GameManager.Instance.ChatMessageServer(null, EChatType.Global, -1,
        "[00ff00]The chat command [ff0000]/horde[-] will teleport you to the community horde base.[-]",
        null, EMessageSender.None);
      _schedule.NextWarning = GetNextWarningTimeSpan(_schedule.NextStartTime, _schedule.NextWarning);
    }

    public static void OnXMLChanged() {
      DynamicProperties properties = WorldEnvironment.Properties;
      if (properties is null) {
        LoadSchedule();
        return;
      }

      var schedule = "";
      var duration = DefaultDurationIrlMinutes;
      properties.ParseString("blood_rain_schedule_irl", ref schedule);
      properties.ParseFloat("blood_rain_duration_irl_minutes", ref duration);
      properties.ParseInt("blood_rain_min_game_day", ref _minGameDay);
      LoadSchedule(schedule, duration);
    }

    public static void LoadSchedule(string scheduleStr = "", float durationIrlMinutes = DefaultDurationIrlMinutes) {
      if (scheduleStr.Length <= 0) {
        SetSchedule(null);
        return;
      }

      CronExpression cronExpression;
      try {
        cronExpression = CronExpression.Parse(scheduleStr);
      } catch (Exception e) {
        Log.Error($"[BloodRain] Could not parse schedule string '{scheduleStr}': {e.Message}");
        SetSchedule(null);
        return;
      }

      DateTime? nextStartTime = GetNextStartTime(cronExpression);
      if (nextStartTime is null) {
        // If there's no future time, no point in storing the schedule
        SetSchedule(null);
        return;
      }

      TimeSpan? nextWarning = GetNextWarningTimeSpan((DateTime)nextStartTime);
      SetSchedule(new BloodRainSchedule(cronExpression, durationIrlMinutes, (DateTime)nextStartTime, nextWarning));
    }

    public static void SetSchedule([CanBeNull] BloodRainSchedule schedule) {
      Log.Out(schedule is null
        ? "[BloodRain] No schedule set"
        : $"[BloodRain] Schedule set to {schedule.CronExpression} with a duration of {schedule.DurationIrlMinutes} IRL minutes");
      _schedule = schedule;
    }

    public static DateTime? GetNextStartTime(CronExpression cronExpression) {
      DateTimeOffset? nextOffset = cronExpression.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
      DateTime? next = nextOffset?.DateTime;
      Log.Out($"[BloodRain] Next blood rain is scheduled for {next}");
      return next;
    }

    public static TimeSpan? GetNextWarningTimeSpan(DateTime nextStartTime, TimeSpan? lastWarning = null) {
      var remaining = (nextStartTime - DateTime.Now).TotalMinutes;
      if (remaining < 1) {
        // Don't warn at 0 minutes
        return null;
      }

      var max = lastWarning is null ? DefaultWarningIrlMinutes : ((TimeSpan)lastWarning).TotalMinutes - 1;
      var next = Math.Floor(Math.Min(max, remaining));
      Log.Out($"[BloodRain] Next warning is scheduled for {next} minutes before the blood rain");
      return TimeSpan.FromMinutes(next);
    }
  }
}
