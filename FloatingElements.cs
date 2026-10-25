using System;
using BrilliantSkies.Ftd.DamageModels;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using BrilliantSkies.Core.Logger;

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

        private TextMeshProUGUI? _mainText;
        private RectTransform? _rectTransform;
        private Image? _damageIconImage;

        public DamageType Type { get; private set; }
        public float PreviousDamage { get; private set; }
        public float TotalDamage { get; private set; }
        public float CurrentDPS { get; private set; }
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
        private bool _textMeshDirty;

        private const int MaxDpsBuckets = 10;
        private readonly float[] _dpsSecondBuckets = new float[MaxDpsBuckets];
        private int _dpsBucketIndex;
        private int _dpsCompletedSeconds;
        private float _damageThisCurrentSecond;
        private float _nextDpsUpdateTime;
        // temp fields
        private float _lastAlpha = -1f;
        private float _lastScale = -1f;

        public bool IsExpired(float currentTime) => (currentTime - CreationTime) >= Lifetime;

        private void Awake()
        {
            EnsureComponents();
        }


        /// <summary>
        /// Initializes and configures required UI components for displaying floating damage numbers if they are not already present.
        /// </summary>
        private void EnsureComponents()
        {
            if (_mainText != null) return;

            // parent the element to the overlay canvas if it exists
            if (FloatingDamageNumberRenderer.CanvasRect != null && transform.parent != FloatingDamageNumberRenderer.CanvasRect)
            {
                transform.SetParent(FloatingDamageNumberRenderer.CanvasRect, false);
            }

            // use TMPuGUI instead of TMPro due to overlay issues on graphic mods
            TMP_FontAsset? fontAsset = GetFontAsset();

            _mainText = gameObject.AddComponent<TextMeshProUGUI>();
            _rectTransform = _mainText.rectTransform;
            _mainText.font = fontAsset;
            _mainText.raycastTarget = false;

            if (fontAsset != null && fontAsset.material != null)
            {
                _mainText.fontSharedMaterial = fontAsset.material;
            }

            _mainText.UpdateMeshPadding();

            _mainText.alignment = TextAlignmentOptions.Midline;
            _mainText.textWrappingMode = TextWrappingModes.NoWrap;
            _mainText.overflowMode = TextOverflowModes.Overflow;
            _mainText.fontSize = 80f; // UI base font size in pixels

            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.sizeDelta = new Vector2(200f, 50f);
            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.zero;

            EnsureSpriteRenderer();
        }

        /// <summary>
        /// Initializes the sprite renderer for the damage icon if it does not already exist.
        /// </summary>
        private void EnsureSpriteRenderer()
        {
            if (_damageIconImage != null) return;

            GameObject imageObj = new("DamageTypeSprite");
            imageObj.transform.SetParent(transform, false);
            _damageIconImage = imageObj.AddComponent<Image>();
            _damageIconImage.raycastTarget = false;
            _damageIconImage.preserveAspect = true;

            RectTransform iconRt = _damageIconImage.rectTransform;
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(80f, 80f); // match font size!!!
        }

        /// <summary>
        /// Initializes the damage number display with the specified parameters.
        /// </summary>
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

            if (DamageNumberSettings.UseDPSMode)
            {
                for (int i = 0; i < MaxDpsBuckets; i++)
                {
                    _dpsSecondBuckets[i] = 0f;
                }
                _damageThisCurrentSecond = initialDamage;
                _dpsBucketIndex = -1;
                _dpsCompletedSeconds = 0;
                _nextDpsUpdateTime = currentTime + 1.0f;
                CurrentDPS = initialDamage;
            }

            float baseLife = DamageNumberSettings.NumberLifetime;
            if (isLargeHit && !DamageNumberSettings.UseDPSMode)
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

            // color and alpha
            _baseColor = DamageNumberSettings.GetColor(damageType);
            _lastAlpha = 1.0f;
            _mainText?.color = _baseColor;
            _damageIconImage?.color = Color.white;

            _popTimer = isLargeHit ? DamageNumberSettings.PopBounceDuration : 0f;

            transform.position = worldHitPosition + DamageNumberSettings.BaseSpawnOffset;
            gameObject.SetActive(true);

            UpdateTextDisplay(true);
            
            if (_mainText != null && _cachedFontAsset != null && _cachedFontAsset.material != null)
            {
                _mainText.fontSharedMaterial = _cachedFontAsset.material;
                _mainText.UpdateMeshPadding();
                _mainText.ForceMeshUpdate();
            }
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

            if (DamageNumberSettings.UseDPSMode)
            {
                _damageThisCurrentSecond += amount;
            }

            float baseLife = DamageNumberSettings.NumberLifetime;
            if (IsLargeHit && !DamageNumberSettings.UseDPSMode)
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

            // Only update displays immediately in standard mode; DPS mode updates on a fixed 1-second cadence
            if (!DamageNumberSettings.UseDPSMode)
            {
                UpdateTextDisplay(false);
            }
        }

        /// <summary>
        /// Marks the damage number as a large hit, updates its lifetime, and refreshes the display accordingly.
        /// </summary>
        public void MarkLargeHit()
        {
            if (!IsLargeHit)
            {
                IsLargeHit = true;
                float extra = DamageNumberSettings.NumberLifetime * (DamageNumberSettings.UseDPSMode ? DamageNumberSettings.LargeHitLifetimeMultiplier : 1f);
                Lifetime = Mathf.Max(Lifetime, (Time.time - CreationTime) + extra);
                UpdateTextDisplay(true);
            }
            _popTimer = DamageNumberSettings.PopBounceDuration;
        }

        /// <summary>
        /// on huge hit (later)
        /// </summary>


        /// <summary>
        /// abbreviates large numbers into a more readable format (e.g., 1.5K, 2.35M, 4.72B) based on the magnitude of the value.
        /// </summary>
        private string LargeNumberAbbrevation(float _value)
        {
            if (DamageNumberSettings.FormatLargeNumbers)
            {
                int mag = (int)(Math.Floor(Math.Log10(_value)) / 3); 
                double divisor = Math.Pow(10, mag*3);

                double shortNumber = _value / divisor;
                string suffix = mag switch
                {
                    0 => "",
                    1 => "K",
                    2 => "M",
                    3 => "B",  // past this is not realistic to see in FTD anyway
                    4 => "T",
                    _ => "",
                };

                // if in millions, show two decimal places, otherwise show one decimal place
                if (mag >= 2)
                {
                    return shortNumber.ToString("0.##") + suffix;
                }
                else
                {
                    return shortNumber.ToString("0.#") + suffix;
                }
                
            }
            return _value.ToString("N0");
        }

        /// <summary>
        /// Updates the damage text display and optionally rebuilds the text mesh.
        /// </summary>true to rebuild the text mesh and icon; otherwise, false.
        private void UpdateTextDisplay(bool rebuildMesh)
        {
            // standard mode
            string damageText;
            damageText = LargeNumberAbbrevation(TotalDamage);

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
                _textMeshDirty = true;
            }

            if (rebuildMesh)
            {
                RefreshTextMeshAndIcon();
            }
        }

        /// <summary>
        /// updates text displayers per call
        /// </summary>
        private void UpdateTextDisplayasDPS(bool rebuildMesh)
        {
            string damageText;
            damageText = LargeNumberAbbrevation(CurrentDPS) + " DPS";

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
                _textMeshDirty = true;
            }
            if (rebuildMesh)
            {
                RefreshTextMeshAndIcon();
            }
        }

        /// <summary>
        /// reupdate renderer and positionings
        /// </summary>
        private void RefreshTextMeshAndIcon()
        {
            if (DamageNumberSettings.ShowAssociatedIcon)
            {
                EnsureSpriteRenderer();
                if (_damageIconImage != null)
                {
                    Sprite? icon = DamageIconCache.GetIcon(Type);
                    _damageIconImage.sprite = icon;
                    _damageIconImage.enabled = (icon != null);

                    if (icon != null && _mainText != null)
                    {
                        Vector2 preferredSize = _mainText.GetPreferredValues();
                        float rightEdge = preferredSize.x * 0.5f;
                        float spacing = 6f;
                        float iconHalfWidth = _damageIconImage.rectTransform.sizeDelta.x * 0.5f;

                        _damageIconImage.rectTransform.anchoredPosition = new Vector2(rightEdge + spacing + iconHalfWidth, 0f);
                    }
                }
            }
            else if (_damageIconImage != null)
            {
                _damageIconImage.enabled = false;
            }

            _textMeshDirty = false;
        }

        /// <summary>
        /// force update of the damage number display, including position, scale, and alpha, based on the current time and camera view.
        /// </summary>
        public void ManualUpdate(float currentTime, float deltaTime, Camera? cam)
        {
            float age = Mathf.Max(0f, currentTime - CreationTime);

            if (age >= Lifetime)
            {
                gameObject.SetActive(false);
                return;
            }

            //// In DPS mode, update display strictly once per second so sustained combat is readable
            if (DamageNumberSettings.UseDPSMode && currentTime >= _nextDpsUpdateTime)
            {
                int windowSize = Mathf.Clamp(Mathf.RoundToInt(DamageNumberSettings.NumberLifetime), 1, MaxDpsBuckets);

                while (currentTime >= _nextDpsUpdateTime)
                {
                    _nextDpsUpdateTime += 1.0f;
                    _dpsBucketIndex = (_dpsBucketIndex + 1) % windowSize;
                    _dpsSecondBuckets[_dpsBucketIndex] = _damageThisCurrentSecond;
                    _damageThisCurrentSecond = 0f;
                    _dpsCompletedSeconds++;
                }

                float totalDamageInWindow = 0f;
                for (int i = 0; i < windowSize; i++)
                {
                    totalDamageInWindow += _dpsSecondBuckets[i];
                }

                int divisor = Mathf.Clamp(_dpsCompletedSeconds, 1, windowSize);
                CurrentDPS = totalDamageInWindow / divisor;

                UpdateTextDisplayasDPS(false);
            }

            if (_textMeshDirty)
            {
                RefreshTextMeshAndIcon();
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
            if (cam != null && _rectTransform != null)
            {
                float dist = Vector3.Distance(cam.transform.position, basePos);
                float refDist = DamageNumberSettings.ReferenceDistance > 0f ? DamageNumberSettings.ReferenceDistance : 40f;

                // Calculate 3D world-space elevation first
                float distFactor = Mathf.Pow(Mathf.Max(5f, dist) / refDist, DamageNumberSettings.DistanceScaleExponent);
                float worldDistanceScale = Mathf.Clamp((DamageNumberSettings.BaseTextScale * refDist) * distFactor, DamageNumberSettings.MinTextScale, DamageNumberSettings.MaxTextScale);

                float elevationFactor = Mathf.Pow(Mathf.Max(1f, dist), DamageNumberSettings.DistanceElevationExponent);
                float distanceElevation = Mathf.Clamp(
                    DamageNumberSettings.DistanceElevationFactor * elevationFactor,
                    DamageNumberSettings.MinDistanceElevation,
                    DamageNumberSettings.MaxDistanceElevation);

                Vector3 worldPos = basePos + DamageNumberSettings.BaseSpawnOffset + (cam.transform.up * distanceElevation);

                // Project 3D world position to 2D Screen Space
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                // Correct position so target isn't behind camera
                if (screenPos.z <= 0f)
                {
                    _mainText?.enabled = false;
                    _damageIconImage?.enabled = false;
                    return;
                }

                if (_mainText != null && !_mainText.enabled) _mainText.enabled = true;
                if (_damageIconImage != null && DamageNumberSettings.ShowAssociatedIcon && _damageIconImage.sprite != null)
                    _damageIconImage.enabled = true;

                // Screen-space scale
                // In 3D, angular size is proportional to (worldScale / dist).
                float baseUiScale = (worldDistanceScale / Mathf.Max(5f, dist)) * (refDist * 1.5f);
                baseUiScale = Mathf.Clamp(baseUiScale, 0.4f, 2.5f);
                baseUiScale *= DamageNumberSettings.BaseTextScaleMultiplier;

                float uiScale = baseUiScale;
                if (_popTimer > 0f)
                {
                    _popTimer -= deltaTime;
                    float t = Mathf.Clamp01(_popTimer / DamageNumberSettings.PopBounceDuration);
                    uiScale *= Mathf.Lerp(1.0f, DamageNumberSettings.PopBounceScale, t);
                }

                if (Mathf.Abs(uiScale - _lastScale) > 0.005f)
                {
                    _lastScale = uiScale;
                    _rectTransform.localScale = new Vector3(uiScale, uiScale, 1f);
                }

                // Screen-space vertical stacking for UI Canvas elements
                float baseFontSize = _mainText != null ? _mainText.fontSize : 80f;
                float stackSpacing = StackIndex * (baseFontSize * baseUiScale * DamageNumberSettings.StackSpacingMultiplier);
                screenPos.y += stackSpacing;

                _rectTransform.position = new Vector3(screenPos.x, screenPos.y, 0f);
                _rectTransform.anchoredPosition = new Vector2(screenPos.x, screenPos.y);
            }

            // Alpha fade out
            float remaining = Lifetime - age;
            float alpha = 1.0f;
            if (remaining < DamageNumberSettings.FadeOutDuration && DamageNumberSettings.FadeOutDuration > 0f)
            {
                alpha = Mathf.Clamp01(remaining / DamageNumberSettings.FadeOutDuration);
            }

            if (Mathf.Abs(alpha - _lastAlpha) > 0.004f)
            {
                _lastAlpha = alpha;
                if (_mainText != null)
                {
                    Color c = _baseColor;
                    c.a = alpha;
                    _mainText.color = c;
                }
                _damageIconImage?.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        private static int _cameraFrame = -1;
        private static Camera? _cachedCamera;

        /// <summary>
        /// unity get active camera
        /// </summary>
        public static Camera? ResolveActiveCamera()
        {
            int frame = Time.frameCount;
            if (frame == _cameraFrame && _cachedCamera != null && _cachedCamera.isActiveAndEnabled)
                return _cachedCamera;
            _cameraFrame = frame;

            // see for camera main
            Camera? cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled)
            {
                _cachedCamera = cam;
                return cam;
            }

            // fallback to the highest-depth active perspective camera rendering to the screen
            var all = Camera.allCameras;
            Camera? bestCam = null;
            float highestDepth = float.MinValue;

            for (int i = 0; i < all.Length; i++)
            {
                Camera c = all[i];
                if (c != null && c.isActiveAndEnabled && !c.orthographic && c.targetTexture == null)
                {
                    if (c.depth >= highestDepth)
                    {
                        highestDepth = c.depth;
                        bestCam = c;
                    }
                }
            }

            _cachedCamera = bestCam ?? (all.Length > 0 ? all[0] : null);
            return _cachedCamera;
        }

        private static Font GetFont()
        {
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

            // Ensure we have a TMP_FontAsset for LiberationSans SDF, which is the default font used by TextMeshPro in FTD.
            foreach (var tmpFont in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (tmpFont != null && tmpFont.name.Contains("LiberationSans SDF") && !tmpFont.name.Contains("Fallback"))
                {
                    _cachedFontAsset = Instantiate(tmpFont);
                    if (_cachedFontAsset.material != null)
                    {
                        _cachedFontAsset.material = new Material(_cachedFontAsset.material);
                    }
                    break;
                }
            }

            if (_cachedFontAsset == null)
            {
                Font font = GetFont();
                if (font == null) return null;
                _cachedFontAsset = TMP_FontAsset.CreateFontAsset(font);
            }

            if (_cachedFontAsset != null && _cachedFontAsset.material != null)
            {
                ShaderUtilities.GetShaderPropertyIDs(); // Ensure TMP shader property IDs are initialized
                Material mat = _cachedFontAsset.material;

                // Prefer full Distance Field Overlay first for reliable OUTLINE_ON support, fallback to Mobile
                Shader? overlayShader = Shader.Find("TextMeshPro/Distance Field Overlay")
                                     ?? Shader.Find("TextMeshPro/Distance Field-Overlay")
                                     ?? Shader.Find("TextMeshPro/Mobile/Distance Field - Overlay");

                if (overlayShader != null)
                {
                    mat.shader = overlayShader;
                }

                mat.EnableKeyword("OUTLINE_ON");
                mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 1f));
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.10f); // outline thickness here
                mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.1f);    // prevents outline from eating into text

                mat.renderQueue = 4500;
                mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                mat.SetInt("_ZWrite", 0);
            }

            return _cachedFontAsset;
        }
    }
}

