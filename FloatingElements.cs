using System;
using BrilliantSkies.Ftd.DamageModels;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace FloatingDamageNumbers
{
    /// <summary>
    /// 3D World-Space Floating Damage Number using Unity's native TextMesh & MeshRenderer.
    /// Billboards to face the camera, scales dynamically with distance
    /// and supports clean camera-aligned vertical stacking for multi-type hits (e.g. APHE).
    /// </summary>
    public class FloatingElements : MonoBehaviour
    {
        private static Font? _cachedFont;
        private static TMP_FontAsset? _cachedFontAsset;

        private TextMeshPro? _mainText;
        private MeshRenderer? _mainRenderer;
        private SpriteRenderer? _damageSpriteRenderer;
        public DamageType Type { get; private set; }
        public float PreviousDamage { get; private set; }
        public float TotalDamage { get; private set; }
        public int HitCount { get; private set; }

        public bool IsLargeHit { get; private set; }
        public float CreationTime { get; private set; }
        public float LastHitTime { get; private set; }
        public float Lifetime { get; private set; }

        public MainConstruct? Construct { get; private set; }
        public int ConstructId { get; private set; }
        public Vector3 LastWorldHitPosition { get; private set; }
        public Vector3 LocalHitPosition { get; private set; }

        /// <summary>
        /// Vertical stack slot (0 = bottom, 1 = first stack above, 2 = second stack, etc.)
        /// Used when multiple damage types (e.g. APHE: Kinetic + Explosive) occur at the same location.
        /// </summary>
        public int StackIndex { get; set; } = 0;

        private float _popTimer;
        private Color _baseColor;

        public bool IsExpired(float currentTime) => (currentTime - CreationTime) >= Lifetime;

        private void Awake()
        {
            EnsureComponents();
        }

        private void EnsureComponents()
        {
            if (_mainText != null) return;

            TMP_FontAsset? fontAsset = GetFontAsset();

            // Main TextMeshPro
            _mainText = gameObject.AddComponent<TextMeshPro>();
            _mainRenderer = gameObject.GetComponent<MeshRenderer>();
            _mainText.font = fontAsset;

            if (_mainRenderer != null && fontAsset != null && fontAsset.material != null)
            {
                _mainRenderer.sharedMaterial = fontAsset.material;
                _mainRenderer.sortingOrder = 9999;
                _mainRenderer.sortingLayerName = "UI";
            }

            // alignment, wrapping, overflow, font size
            _mainText.alignment = TextAlignmentOptions.Midline;
            _mainText.textWrappingMode = TextWrappingModes.NoWrap;
            _mainText.overflowMode = TextOverflowModes.Overflow;
            _mainText.fontSize = 16f;

            // pop scaling from middle
            RectTransform rt = _mainText.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(10f, 2f);

            // outline
            _mainText.fontMaterial.EnableKeyword("OUTLINE_ON");
            _mainText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.05f);

            EnsureSpriteRenderer();
        }

        private void EnsureSpriteRenderer()
        {
            if (_damageSpriteRenderer != null) return;

            GameObject imageObj = new("DamageTypeSprite");
            imageObj.transform.SetParent(transform, false);
            _damageSpriteRenderer = imageObj.AddComponent<SpriteRenderer>();
            _damageSpriteRenderer.sortingOrder = 10000;
            _damageSpriteRenderer.sortingLayerName = "UI";
            imageObj.transform.localPosition = new Vector3(1f, 0f, -0.01f);
        }

        public void Initialize(
            MainConstruct? construct,
            DamageType damageType,
            float initialDamage,
            Vector3 worldHitPosition,  // if SIMPLE mode is on, this will be the COM of the construct, otherwise it will be the actual hit position'
            bool isLargeHit,
            float currentTime)
        {
            EnsureComponents();

            Construct = construct;
            ConstructId = construct != null ? construct.UniqueId : -1;
            Type = damageType;
            TotalDamage = initialDamage;
            PreviousDamage = 0f;
            HitCount = 1;
            IsLargeHit = isLargeHit;
            CreationTime = currentTime;
            LastHitTime = currentTime;
            StackIndex = 0;

            float baseLife = DamageNumberSettings.NumberLifetime;
            if (isLargeHit)
            {
                baseLife *= DamageNumberSettings.LargeHitLifetimeMultiplier;
            }
            Lifetime = baseLife;
            LastWorldHitPosition = worldHitPosition;
            if (construct != null)
            {
                try
                {
                    LocalHitPosition = construct.SafeGlobalToLocal(worldHitPosition);
                }
                catch
                {
                    LocalHitPosition = worldHitPosition;
                }
            }
            else
            {
                LocalHitPosition = worldHitPosition;
            }

            _baseColor = DamageNumberSettings.GetColor(damageType);
            _popTimer = isLargeHit ? DamageNumberSettings.PopBounceDuration : 0f;

            UpdateTextDisplay();

            transform.position = worldHitPosition + DamageNumberSettings.BaseSpawnOffset;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Smoothly adds damage to this active instance without causing visual jitter.
        /// </summary>
        public void AddDamage(float amount, Vector3 newWorldHitPos, float currentTime, bool isRapidOrExplosion)
        {
            PreviousDamage = TotalDamage;
            TotalDamage += amount;
            HitCount++;
            LastHitTime = currentTime;

            float baseLife = DamageNumberSettings.NumberLifetime;
            if (IsLargeHit)
            {
                baseLife *= DamageNumberSettings.LargeHitLifetimeMultiplier;
            }
            Lifetime = (currentTime - CreationTime) + baseLife;

            // Smooths out new positions for high RPM weapondry or spam.
            if (DamageNumberSettings.HoverOverLastPositionHit && DamageNumberSettings.EnableDetailedSettings)
            {
                if (Construct != null)
                {
                    try
                    {
                        Vector3 targetLocal = Construct.SafeGlobalToLocal(newWorldHitPos);
                        float blendFactor = isRapidOrExplosion ? 0.06f : 0.25f;
                        LocalHitPosition = Vector3.Lerp(LocalHitPosition, targetLocal, blendFactor);
                    }
                    catch
                    {
                        LocalHitPosition = Vector3.Lerp(LocalHitPosition, newWorldHitPos, 0.1f);
                    }
                }
                else
                {
                    LastWorldHitPosition = Vector3.Lerp(LastWorldHitPosition, newWorldHitPos, 0.1f);
                }
            }

            // Only trigger a pop bounce for hits that meet or exceed LargeHitThreshold
            if (amount >= DamageNumberSettings.LargeHitThreshold)
            {
                _popTimer = DamageNumberSettings.PopBounceDuration;
            }

            UpdateTextDisplay();
        }

        public void MarkLargeHit()
        {
            if (!IsLargeHit)
            {
                IsLargeHit = true;
                float extra = DamageNumberSettings.NumberLifetime * DamageNumberSettings.LargeHitLifetimeMultiplier;
                Lifetime = Mathf.Max(Lifetime, (Time.time - CreationTime) + extra);
                UpdateTextDisplay();
            }
            _popTimer = DamageNumberSettings.PopBounceDuration;
        }

        private void UpdateTextDisplay()
        {
            string damageText;
            if (DamageNumberSettings.FormatAbbreviatedThousands && TotalDamage >= 1000f)
            {
                damageText = (TotalDamage / 1000f).ToString("0.#") + "k";
            }
            else
            {
                damageText = Mathf.RoundToInt(TotalDamage).ToString("N0");
            }

            if (DamageNumberSettings.ShowDamageTypeTag)
            {
                if (Type == DamageType.None)
                {
                    damageText = $"[OTHER] {damageText}";
                }
                else
                {
                    string str_type = Type.ToString().ToUpper();
                    damageText = $"[{str_type}] {damageText}";
                }
            }

            if (_mainText != null)
            {
                _mainText.text = damageText;
                _mainText.ForceMeshUpdate(); // updates renderedWidth immediately
            }

            // image icon for damage type
            if (DamageNumberSettings.ShowAssociatedIcon)
            {
                EnsureSpriteRenderer();
                if (_damageSpriteRenderer != null)
                {
                    Sprite? icon = DamageIconCache.GetIcon(Type);
                    _damageSpriteRenderer.sprite = icon;
                    _damageSpriteRenderer.enabled = (icon != null);

                    if (icon != null && _mainText != null)
                    {
                        float rightEdge = _mainText.renderedWidth * 0.5f;
                        float spacing = 0.25f; // gap between text and icon
                        float spriteHalfWidth = (icon.rect.width / icon.pixelsPerUnit) * _damageSpriteRenderer.transform.localScale.x * 0.5f;

                        _damageSpriteRenderer.transform.localPosition = new Vector3(rightEdge + spacing + spriteHalfWidth, 0f, -0.01f);
                    }
                }
            }
            else
            {
                _damageSpriteRenderer?.enabled = false;
            }
        }

        private void LateUpdate()
        {
            float time = Time.time;
            float age = Mathf.Max(0f, time - CreationTime);

            if (age >= Lifetime)
            {
                gameObject.SetActive(false);
                return;
            }

            // Resolve the 3D world position of the damage number so we can anchor to the construct
            Vector3 basePos = LastWorldHitPosition;
            if (DamageNumberSettings.TrackConstructMovement && Construct != null)
            {
                try
                {
                    basePos = Construct.SafeLocalToGlobal(LocalHitPosition);
                    LastWorldHitPosition = basePos;
                }
                catch
                {
                    basePos = LastWorldHitPosition;
                }
            }

            // Camera and billboard alignment
            Camera? cam = GetCamera();
            if (cam != null)
            {
                transform.rotation = cam.transform.rotation;

                float dist = Vector3.Distance(cam.transform.position, basePos);
                float baseScale = DamageNumberSettings.BaseTextScale;

                // Sub-linear distance factor (e.g. dist^0.50) prevents distant numbers from ballooning over targets, this is very prevalant on max engagement range.
                float refDist = DamageNumberSettings.ReferenceDistance > 0f ? DamageNumberSettings.ReferenceDistance : 40f;
                float distFactor = Mathf.Pow(Mathf.Max(5f, dist) / refDist, DamageNumberSettings.DistanceScaleExponent);
                float distanceScale = Mathf.Clamp((baseScale * refDist) * distFactor, DamageNumberSettings.MinTextScale, DamageNumberSettings.MaxTextScale);
                float renderScale = distanceScale;


                // Pop bounce effect for new hits, this is a quick scale-up and then back down to normal size.
                if (_popTimer > 0f)
                {
                    _popTimer -= Time.deltaTime;
                    float t = Mathf.Clamp01(_popTimer / DamageNumberSettings.PopBounceDuration);
                    renderScale *= Mathf.Lerp(1.0f, DamageNumberSettings.PopBounceScale, t);
                }

                transform.localScale = Vector3.one * renderScale;

                // Sub-linear distance elevation with ceiling clamp:
                // Prevents distant numbers from rocketing into the sky while ensuring close-range targets float clearly above the hull.
                float elevationFactor = Mathf.Pow(Mathf.Max(1f, dist), DamageNumberSettings.DistanceElevationExponent);
                float distanceElevation = Mathf.Clamp(
                    DamageNumberSettings.DistanceElevationFactor * elevationFactor,
                    DamageNumberSettings.MinDistanceElevation,
                    DamageNumberSettings.MaxDistanceElevation);

                float stackSpacing = StackIndex * (distanceScale * DamageNumberSettings.StackSpacingMultiplier);
                Vector3 upVector = cam.transform.up;

                transform.position = basePos + DamageNumberSettings.BaseSpawnOffset 
                                     + (upVector * (distanceElevation + stackSpacing));
            }
            else
            {
                float stackSpacing = StackIndex * 1.5f;
                transform.position = basePos + DamageNumberSettings.BaseSpawnOffset + new Vector3(0f, stackSpacing, 0f);
            }

            // Alpha fade out
            float remaining = Lifetime - age;
            float alpha = 1.0f;
            if (remaining < DamageNumberSettings.FadeOutDuration && DamageNumberSettings.FadeOutDuration > 0f)
            {
                alpha = Mathf.Clamp01(remaining / DamageNumberSettings.FadeOutDuration);
            }

            if (_mainText != null)
            {
                Color c = _baseColor;
                c.a = alpha;
                _mainText.color = c;
            }

            if (_damageSpriteRenderer != null)
            {
                _damageSpriteRenderer.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        private static int _cameraFrame = -1;
        private static Camera? _cachedCamera;

        private static Camera? GetCamera()
        {
            // Cache the camera lookup once per frame to avoid repeated Camera.allCameras allocations
            int frame = Time.frameCount;
            if (frame == _cameraFrame) return _cachedCamera;
            _cameraFrame = frame;

            Camera? cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled) { _cachedCamera = cam; return cam; }
            cam = Camera.current;
            if (cam != null && cam.isActiveAndEnabled) { _cachedCamera = cam; return cam; }

            var all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].isActiveAndEnabled && !all[i].orthographic && all[i].targetTexture == null)
                {
                    _cachedCamera = all[i];
                    return all[i];
                }
            }
            _cachedCamera = all.Length > 0 ? all[0] : null;
            return _cachedCamera;
        }

        private static Font GetFont()
        {
            // there's that one font I want to grab that's used in the Hologram Projectors but I don't know the name of it.
            if (_cachedFont != null) return _cachedFont;

            try
            {
                _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch { }

            if (_cachedFont == null)
            {
                try
                {
                    _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch { }
            }

            if (_cachedFont == null)
            {
                try
                {
                    var fonts = Resources.FindObjectsOfTypeAll<Font>();
                    if (fonts != null && fonts.Length > 0)
                    {
                        _cachedFont = fonts[0];
                    }
                }
                catch { }
            }

            if (_cachedFont == null)
            {
                try
                {
                    _cachedFont = Font.CreateDynamicFontFromOSFont("Arial", 32);
                }
                catch { }
            }

            return _cachedFont!;
        }

        private static TMP_FontAsset? GetFontAsset()
        {
            if (_cachedFontAsset != null) return _cachedFontAsset;

            Font font = GetFont();
            if (font == null) return null;

            _cachedFontAsset = TMP_FontAsset.CreateFontAsset(font);
            return _cachedFontAsset;
        }
    }
}

