// Plays SfxSynth recipes through Unity audio: one-shots are rendered to
// short clips on demand, the theme is rendered once to a looping clip.
// Mute choices persist in PlayerPrefs (browser keys were localStorage).
using UnityEngine;

namespace Territory.Client.Unity
{
    public sealed class SfxPlayer : MonoBehaviour
    {
        public GameClient client;
        public AudioSource effectsSource;
        public AudioSource themeSource;

        private SfxSynth Theme => client != null ? client.synth : null;

        void Awake()
        {
            if (client == null) client = FindObjectOfType<GameClient>();
            if (effectsSource == null)
            {
                effectsSource = gameObject.AddComponent<AudioSource>();
                effectsSource.playOnAwake = false;
            }
            if (themeSource == null)
            {
                themeSource = gameObject.AddComponent<AudioSource>();
                themeSource.playOnAwake = false;
                themeSource.loop = true;
            }
            ApplyPrefs();
        }

        public void ApplyPrefs()
        {
            bool muted = PlayerPrefs.GetInt("territory-mute", 0) == 1;
            bool musicMuted = PlayerPrefs.GetInt("territory-music-mute", 0) == 1;
            effectsSource.mute = muted;
            themeSource.mute = muted || musicMuted;
            if (Theme != null)
            {
                Theme.SetMuted(muted);
                Theme.SetMusicMuted(musicMuted);
            }
        }

        public void ToggleMuted()
        {
            bool muted = PlayerPrefs.GetInt("territory-mute", 0) != 1;
            PlayerPrefs.SetInt("territory-mute", muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyPrefs();
        }

        public void ToggleMusicMuted()
        {
            bool muted = PlayerPrefs.GetInt("territory-music-mute", 0) != 1;
            PlayerPrefs.SetInt("territory-music-mute", muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyPrefs();
        }

        public bool IsMuted => PlayerPrefs.GetInt("territory-mute", 0) == 1;
        public bool IsMusicMuted => PlayerPrefs.GetInt("territory-music-mute", 0) == 1;

        private void OneShot(System.Action<SfxSynth> recipe)
        {
            if (IsMuted) return;
            var synth = new SfxSynth();
            recipe(synth);
            double length = 2.0;
            float[] data = synth.Render(length);
            var clip = AudioClip.Create("sfx", data.Length, 1, SfxSynth.SampleRate, false);
            clip.SetData(data, 0);
            effectsSource.PlayOneShot(clip);
        }

        public void PlayCapture(double size, double loudness) => OneShot(s => s.Capture(size, loudness));
        public void PlayDeath(double loudness) => OneShot(s => s.Death(loudness));
        public void PlayTick() => OneShot(s => s.TickSfx());
        public void PlayGo() => OneShot(s => s.Go());
        public void PlayWinLose(CardScores scores)
        {
            bool mine = false, listed = false;
            if (scores != null && client.myId != null)
            {
                foreach (var row in scores.List)
                {
                    if (row.Id != client.myId.Value) continue;
                    listed = true;
                    mine = scores.WinnerId == row.Id;
                }
            }
            if (mine) OneShot(s => s.Win());
            else if (listed) OneShot(s => s.Lose());
        }

        // Renders the ~8.3s theme loop once and loops it behind the round.
        public void EnsureTheme()
        {
            if (themeSource.clip != null || Theme == null) return;
            var synth = new SfxSynth();
            synth.StartMusic();
            for (double t = 0; t <= 9; t += 0.2) synth.PumpMusic(t);
            float[] data = synth.Render(32 * SfxSynth.MusicStep + 0.5);
            var clip = AudioClip.Create("theme", data.Length, 1, SfxSynth.SampleRate, false);
            clip.SetData(data, 0);
            themeSource.clip = clip;
            themeSource.volume = 0.8f;
            if (!IsMuted && !IsMusicMuted) themeSource.Play();
        }

        void Update()
        {
            if (themeSource.clip != null && !themeSource.isPlaying && !IsMuted && !IsMusicMuted)
                themeSource.Play();
        }
    }
}
