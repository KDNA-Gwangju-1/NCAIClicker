#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NCAIClicker.Demo
{
    /// <summary>
    /// 시연 중 기록한 자막을 ASS 자막 파일로 쓴다 (#344). 녹화 영상과 같은 이름으로 두면
    /// 편집 단계에서 ffmpeg 로 그대로 입힌다. 문구만 고칠 때는 이 파일만 고쳐 다시 인코딩한다.
    /// 스타일은 docs/SUBMISSION_VIDEO.md 자막 원칙(하단 중앙, 흰 글자 + 검은 반투명 띠)을 따른다.
    /// </summary>
    public class DemoCaptionTrack
    {
        private const string Header =
            "[Script Info]\n" +
            "ScriptType: v4.00+\n" +
            "PlayResX: 1920\n" +
            "PlayResY: 1080\n" +
            "\n" +
            "[V4+ Styles]\n" +
            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
            "Style: Sub,NanumGothic,56,&H00FFFFFF,&H00FFFFFF,&H80000000,&H80000000,1,0,0,0,100,100,0,0,3,18,0,2,40,40,70,1\n" +
            "\n" +
            "[Events]\n" +
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n";

        private const float MinVisibleSeconds = 2.5f;

        private readonly List<(float Start, float End, string Text)> _cues = new List<(float, float, string)>();

        public int Count => _cues.Count;

        /// <summary>
        /// 자막 한 장을 더한다. 앞 자막이 아직 떠 있으면 앞 자막을 이 시각에 끝내되, 앞 자막이 최소 시간도
        /// 못 채웠으면 이 자막을 그만큼 늦춘다 — 사건이 몰려도 읽을 틈을 남긴다.
        /// </summary>
        public void AddCue(float start, float duration, string text)
        {
            if (_cues.Count > 0)
            {
                var last = _cues[_cues.Count - 1];
                start = Mathf.Max(start, last.Start + MinVisibleSeconds);
                if (last.End > start)
                {
                    _cues[_cues.Count - 1] = (last.Start, start, last.Text);
                }
            }
            _cues.Add((start, start + duration, text));
        }

        public void WriteTo(string path)
        {
            var builder = new StringBuilder(Header);
            foreach (var cue in _cues)
            {
                var text = cue.Text.Replace("\r", string.Empty).Replace("\n", "\\N");
                builder.Append($"Dialogue: 0,{FormatTime(cue.Start)},{FormatTime(cue.End)},Sub,,0,0,0,,{text}\n");
            }
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f)
            {
                seconds = 0f;
            }
            var centis = (int)(seconds * 100f + 0.5f);
            return $"{centis / 360000}:{centis / 6000 % 60:00}:{centis / 100 % 60:00}.{centis % 100:00}";
        }
    }
}
#endif
