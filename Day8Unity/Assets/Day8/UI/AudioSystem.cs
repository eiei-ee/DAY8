using System.Collections;
using UnityEngine;

namespace Day8.UI {

    /// 程序化音频：无外部资源，全部运行时合成。
    /// 心跳随能量变化；扫描音标记它开口；警报对应第八天断电；夜间音对应记忆整合。
    public class AudioSystem : MonoBehaviour {

        AudioSource _src;
        AudioClip _thump, _blip, _alarm, _night, _tick;
        float _energy = 0.6f;
        bool _alive = true;

        const int Sr = 22050;

        public static AudioSystem Create(Transform parent) {
            var go = new GameObject("AudioSystem");
            go.transform.SetParent(parent, false);
            return go.AddComponent<AudioSystem>();
        }

        void Awake() {
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _thump = Damped(52f, 0.30f, 0.9f, 3.5f);            // 低频心跳
            _blip = Sweep(1250f, 1750f, 0.13f, 0.20f, 2.0f);    // 高频扫描
            _night = Sweep(520f, 330f, 0.9f, 0.16f, 1.2f);      // 夜间整合
            _tick = Sweep(880f, 860f, 0.022f, 0.10f, 5f);       // 打字机微声
            _alarm = Alarm();
            StartCoroutine(Heartbeat());
        }

        void OnDestroy() => _alive = false;

        /// 阻尼正弦：f0 衰减到 e^{-d t}
        AudioClip Damped(float freq, float dur, float vol, float decay) {
            var clip = AudioClip.Create("tone", (int)(Sr * dur), 1, Sr, false);
            var data = new float[(int)(Sr * dur)];
            for (int i = 0; i < data.Length; i++) {
                float t = i / (float)Sr;
                data[i] = vol * Mathf.Sin(2 * Mathf.PI * freq * t) * Mathf.Exp(-decay * t);
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// 频率扫描 + 淡入淡出包络
        AudioClip Sweep(float f0, float f1, float dur, float vol, float decay) {
            var clip = AudioClip.Create("sweep", (int)(Sr * dur), 1, Sr, false);
            var data = new float[(int)(Sr * dur)];
            float phase = 0f;
            for (int i = 0; i < data.Length; i++) {
                float t = i / (float)Sr;
                float f = Mathf.Lerp(f0, f1, t / dur);
                phase += 2 * Mathf.PI * f / Sr;
                float env = Mathf.Sin(Mathf.PI * t / dur) * Mathf.Exp(-decay * t * 0.3f);
                data[i] = vol * Mathf.Sin(phase) * env;
            }
            clip.SetData(data, 0);
            return clip;
        }

        AudioClip Alarm() {
            float dur = 1.8f;
            var clip = AudioClip.Create("alarm", (int)(Sr * dur), 1, Sr, false);
            var data = new float[(int)(Sr * dur)];
            for (int i = 0; i < data.Length; i++) {
                float t = i / (float)Sr;
                float f = (Mathf.FloorToInt(t / 0.45f) % 2 == 0) ? 440f : 554f;
                float env = Mathf.Sin(Mathf.PI * (t % 0.45f) / 0.45f);
                data[i] = 0.14f * Mathf.Sin(2 * Mathf.PI * f * t) * env;
            }
            clip.SetData(data, 0);
            return clip;
        }

        IEnumerator Heartbeat() {
            while (_alive) {
                _src.PlayOneShot(_thump, 0.55f);
                // 能量越低心跳越急促
                yield return new WaitForSeconds(Mathf.Lerp(1.45f, 0.65f, 1f - _energy));
            }
        }

        public void SetEnergy(float e) => _energy = Mathf.Clamp01(e);

        public void Blip() => _src.PlayOneShot(_blip, 0.5f);      // 它开口
        public void Tick() => _src.PlayOneShot(_tick, 0.35f);     // 打字机
        public void Night() => _src.PlayOneShot(_night, 0.5f);    // 记忆整合
        public void PowerFail() => _src.PlayOneShot(_alarm, 0.8f);
    }
}
