using System.Collections.Generic;
using InControl;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// A second Knight driven by its own controller. It isn't a real HeroController
    /// (the game only supports one); it borrows the Knight's sprites and animations,
    /// does its own simple platforming against the level's terrain, hits enemies
    /// through their HealthManager and takes damage from anything with DamageHero.
    /// </summary>
    public class PlayerTwo : MonoBehaviour
    {
        // Movement roughly matched to the Knight.
        const float JumpHoldMax = 0.2f;
        const float JumpCutSpeed = 4f;
        const float Gravity = 55f;
        const float DashTime = 0.25f;
        const float DashCooldown = 0.6f;
        const float AttackCooldown = 0.41f;
        const float AttackAnimTime = 0.3f;
        const float PogoSpeed = 13f;
        const float WallSlideSpeed = 6f;
        const float WallJumpLockTime = 0.15f;
        const float CoyoteTime = 0.1f;
        const float JumpBufferTime = 0.12f;
        const float InvulnTime = 1.3f;
        const float KnockbackLock = 0.2f;
        const float Skin = 0.02f;
        const float StickDeadzone = 0.35f;
        // Draw just behind the Knight but in front of the scenery and fog behind him.
        const float DepthOffset = 0.001f;

        public InputDevice Device;

        // Copied from the Knight's own HeroController values in Init (these are fallbacks).
        float RunSpeed = 8.3f;
        float JumpSpeed = 16.65f;
        float MaxFallSpeed = 20.9f;
        float DashSpeed = 20f;

        HeroController hero;
        tk2dSprite sprite;
        tk2dSpriteAnimator anim;
        MeshRenderer meshRenderer;
        Vector2 size = new Vector2(0.5f, 1.28f);
        Vector2 offset = Vector2.zero;
        int terrainMask;
        Color tint;
        float z;

        Vector2 vel;
        bool grounded;
        bool facingRight = true;
        bool jumping;
        bool usedDoubleJump;
        bool usedAirDash;
        bool wallSliding;
        bool altSlash;
        int wallDir;
        float coyote;
        float jumpBuffer;
        float jumpHold;
        float dashTimer;
        float dashCooldown;
        float attackCooldown;
        float attackAnimTimer;
        float controlLock;
        float wallJumpLock;

        int masks;
        float invuln;
        bool downed;
        float downedTimer;
        int hitsTowardHeal;
        bool heroWasDead;

        bool hidden = true;
        float offscreenTimer;
        string currentClip;
        string attackClip = "Slash";
        readonly HashSet<HealthManager> hitThisSwing = new HashSet<HealthManager>();

        static int MaxMasks
        {
            get { return Mathf.Max(1, LocalCoop.Settings.P2MaxMasks); }
        }

        public static PlayerTwo Create(HeroController hero)
        {
            var heroSprite = hero.GetComponent<tk2dSprite>();
            var heroAnim = hero.GetComponent<tk2dSpriteAnimator>();
            if (heroSprite == null || heroAnim == null)
            {
                LocalCoop.Info("Couldn't find the Knight's sprite; Player 2 not spawned.");
                return null;
            }

            var go = new GameObject("LocalCoop Player 2");
            DontDestroyOnLoad(go);
            go.transform.position = hero.transform.position;

            tk2dSprite spr = tk2dSprite.AddComponent(go, heroSprite.Collection, heroSprite.spriteId);
            tk2dSpriteAnimator an = go.AddComponent<tk2dSpriteAnimator>();
            an.Library = heroAnim.Library;

            PlayerTwo p2 = go.AddComponent<PlayerTwo>();
            p2.Init(hero, spr, an);
            return p2;
        }

        void Init(HeroController h, tk2dSprite spr, tk2dSpriteAnimator an)
        {
            hero = h;
            sprite = spr;
            anim = an;
            meshRenderer = GetComponent<MeshRenderer>();

            var heroRenderer = hero.GetComponent<MeshRenderer>();
            if (heroRenderer != null && meshRenderer != null)
            {
                meshRenderer.sortingLayerID = heroRenderer.sortingLayerID;
                meshRenderer.sortingOrder = heroRenderer.sortingOrder;
            }

            var heroBox = hero.GetComponent<BoxCollider2D>();
            if (heroBox != null)
            {
                size = heroBox.size;
                offset = new Vector2(0f, heroBox.offset.y);
            }

            terrainMask = LayerMask.GetMask("Terrain");
            if (terrainMask == 0) terrainMask = 1 << 8;

            RunSpeed = HeroFloat("RUN_SPEED", RunSpeed);
            JumpSpeed = HeroFloat("JUMP_SPEED", JumpSpeed);
            MaxFallSpeed = HeroFloat("MAX_FALL_VELOCITY", MaxFallSpeed);
            DashSpeed = HeroFloat("DASH_SPEED", DashSpeed);
            LocalCoop.Info("Player 2 speeds: run " + RunSpeed + ", jump " + JumpSpeed +
                           ", fall " + MaxFallSpeed + ", dash " + DashSpeed);

            var s = LocalCoop.Settings;
            tint = new Color(s.TintR, s.TintG, s.TintB, 1f);
            masks = MaxMasks;
            z = hero.transform.position.z + DepthOffset;
            SetHidden(true);
        }

        // ---------------------------------------------------------------- main loop

        void Update()
        {
            if (hero == null)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return; // paused

            if (hero.cState.dead) heroWasDead = true;

            bool canShow = Game.IsPlaying
                           && !hero.cState.transitioning
                           && !hero.cState.dead;
            if (!canShow)
            {
                SetHidden(true);
                return;
            }

            if (hidden)
            {
                SetHidden(false);
                SnapToHero();
            }

            if (heroWasDead)
            {
                heroWasDead = false;
                FullHeal();
            }

            if (PlayerData.instance != null && PlayerData.instance.GetBool("atBench"))
            {
                FullHeal();
            }

            Tick(dt);
            RefreshBindings(dt);

            if (downed)
            {
                downedTimer -= dt;
                if (downedTimer <= 0f)
                {
                    FullHeal();
                    invuln = InvulnTime;
                    SnapToHero();
                }
            }
            else
            {
                HandleInput();
                Simulate(dt);
                CheckHurt();
                Leash(dt);
            }

            UpdateVisuals();
        }

        void Tick(float dt)
        {
            coyote -= dt;
            jumpBuffer -= dt;
            jumpHold -= dt;
            dashTimer -= dt;
            dashCooldown -= dt;
            attackCooldown -= dt;
            attackAnimTimer -= dt;
            controlLock -= dt;
            wallJumpLock -= dt;
            invuln -= dt;
        }

        // ---------------------------------------------------------------- input

        void HandleInput()
        {
            float x = 0f, y = 0f;
            bool jumpPressed = false, jumpDown = false, attackPressed = false, dashPressed = false;

            if (Device != null)
            {
                x = Axis("right") - Axis("left");
                y = Axis("up") - Axis("down");
                jumpPressed = Pressed("jump");
                jumpDown = Held("jump");
                attackPressed = Pressed("attack");
                dashPressed = Pressed("dash");
            }

            int moveX = x > StickDeadzone ? 1 : (x < -StickDeadzone ? -1 : 0);
            int aimY = y > 0.5f ? 1 : (y < -0.5f ? -1 : 0);

            // Horizontal movement
            if (dashTimer > 0f)
            {
                vel.x = (facingRight ? 1f : -1f) * DashSpeed;
                vel.y = 0f;
            }
            else if (controlLock <= 0f && wallJumpLock <= 0f)
            {
                vel.x = moveX * RunSpeed;
                if (moveX != 0) facingRight = moveX > 0;
            }

            // Jumping
            if (jumpPressed) jumpBuffer = JumpBufferTime;
            if (jumpBuffer > 0f && dashTimer <= 0f && controlLock <= 0f)
            {
                if (grounded || coyote > 0f)
                {
                    DoJump(JumpSpeed);
                }
                else if (wallSliding)
                {
                    DoJump(JumpSpeed);
                    vel.x = -wallDir * RunSpeed;
                    facingRight = wallDir < 0;
                    wallJumpLock = WallJumpLockTime;
                    wallSliding = false;
                    usedDoubleJump = false;
                    usedAirDash = false;
                }
                else if (!usedDoubleJump && HasAbility("hasDoubleJump"))
                {
                    usedDoubleJump = true;
                    DoJump(JumpSpeed * 0.9f);
                }
            }

            // Short hop when jump is let go early
            if (jumping && !jumpDown)
            {
                if (vel.y > JumpCutSpeed) vel.y = JumpCutSpeed;
                jumping = false;
                jumpHold = 0f;
            }
            // Dash
            if (dashPressed && dashTimer <= 0f && dashCooldown <= 0f && HasAbility("hasDash")
                && (grounded || !usedAirDash))
            {
                if (wallSliding) facingRight = wallDir < 0;
                else if (moveX != 0) facingRight = moveX > 0;
                dashTimer = DashTime;
                dashCooldown = DashCooldown;
                if (!grounded) usedAirDash = true;
                jumping = false;
                vel = new Vector2((facingRight ? 1f : -1f) * DashSpeed, 0f);
            }

            // Attack
            if (attackPressed && attackCooldown <= 0f && dashTimer <= 0f)
            {
                if (wallSliding) facingRight = wallDir < 0;
                Attack(aimY);
            }

            lastMoveX = moveX;
            lastJumpDown = jumpDown;
        }

        int lastMoveX;
        bool lastJumpDown;

        // Player 2 uses the same controller bindings as the game (Options → Controller).
        // Defaults are only used if the game's bindings can't be read.
        static readonly string[] BoundActions = { "left", "right", "up", "down", "jump", "attack", "dash" };
        static readonly Dictionary<string, string[]> DefaultBindings = new Dictionary<string, string[]>
        {
            { "left", new[] { "LeftStickLeft", "DPadLeft" } },
            { "right", new[] { "LeftStickRight", "DPadRight" } },
            { "up", new[] { "LeftStickUp", "DPadUp" } },
            { "down", new[] { "LeftStickDown", "DPadDown" } },
            { "jump", new[] { "Action1" } },
            { "attack", new[] { "Action3" } },
            { "dash", new[] { "RightTrigger" } },
        };
        readonly Dictionary<string, List<InputControlType>> bindings = new Dictionary<string, List<InputControlType>>();
        float bindingRefresh;
        bool loggedBindings;

        void RefreshBindings(float dt)
        {
            bindingRefresh -= dt;
            if (bindingRefresh > 0f && bindings.Count > 0) return;
            bindingRefresh = 2f; // pick up rebinds made in the options menu

            var summary = new System.Text.StringBuilder();
            foreach (string action in BoundActions)
            {
                List<InputControlType> list = Game.BoundControls(action);
                if (list.Count == 0)
                {
                    foreach (string name in DefaultBindings[action])
                    {
                        InputControlType? c = Game.Control(name);
                        if (c.HasValue) list.Add(c.Value);
                    }
                }
                bindings[action] = list;
                summary.Append(action).Append('=').Append(string.Join("/", list.ConvertAll(c => c.ToString()).ToArray())).Append(' ');
            }
            if (!loggedBindings)
            {
                loggedBindings = true;
                LocalCoop.Info("Player 2 bindings: " + summary);
            }
        }

        float Axis(string action)
        {
            List<InputControlType> list;
            if (Device == null || !bindings.TryGetValue(action, out list)) return 0f;
            float best = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                InputControl c = Device.GetControl(list[i]);
                if (c != null && c.Value > best) best = c.Value;
            }
            return best;
        }

        bool Held(string action)
        {
            List<InputControlType> list;
            if (Device == null || !bindings.TryGetValue(action, out list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                InputControl c = Device.GetControl(list[i]);
                if (c != null && c.IsPressed) return true;
            }
            return false;
        }

        bool Pressed(string action)
        {
            List<InputControlType> list;
            if (Device == null || !bindings.TryGetValue(action, out list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                InputControl c = Device.GetControl(list[i]);
                if (c != null && c.WasPressed) return true;
            }
            return false;
        }

        void DoJump(float speed)
        {
            vel.y = speed;
            jumping = true;
            jumpHold = JumpHoldMax;
            jumpBuffer = 0f;
            coyote = 0f;
            grounded = false;
        }

        // ---------------------------------------------------------------- physics

        void Simulate(float dt)
        {
            if (dashTimer <= 0f)
            {
                bool holdingJump = jumping && jumpHold > 0f && lastJumpDown;
                if (!holdingJump) vel.y -= Gravity * dt;
                float maxFall = wallSliding ? WallSlideSpeed : MaxFallSpeed;
                if (vel.y < -maxFall) vel.y = -maxFall;
            }
            if (vel.y <= 0f) jumping = false;

            Vector2 pos = transform.position;
            bool hit;

            // Horizontal
            float dx = vel.x * dt;
            if (dx != 0f)
            {
                float sign = Mathf.Sign(dx);
                float moved = Cast(pos, new Vector2(sign, 0f), Mathf.Abs(dx), out hit);
                pos.x += sign * moved;
                if (hit)
                {
                    vel.x = 0f;
                    if (dashTimer > 0f) dashTimer = 0f;
                }
            }

            // Vertical
            float dy = vel.y * dt;
            if (dy != 0f)
            {
                float sign = Mathf.Sign(dy);
                float moved = Cast(pos, new Vector2(0f, sign), Mathf.Abs(dy), out hit);
                pos.y += sign * moved;
                if (hit)
                {
                    vel.y = 0f;
                    jumping = false;
                }
            }

            grounded = vel.y <= 0f && Touching(pos, Vector2.down);
            if (grounded)
            {
                coyote = CoyoteTime;
                usedDoubleJump = false;
                usedAirDash = false;
            }

            // Wall slide: airborne, falling, pushing into a wall
            wallDir = 0;
            if (!grounded && lastMoveX != 0 && Touching(pos, new Vector2(lastMoveX, 0f)))
            {
                wallDir = lastMoveX;
            }
            wallSliding = wallDir != 0 && vel.y <= 0f && dashTimer <= 0f && HasAbility("hasWalljump");

            transform.position = new Vector3(pos.x, pos.y, z);
        }

        /// <summary>
        /// Sweeps the body box against terrain. Returns how far it can move (≤ dist).
        /// </summary>
        float Cast(Vector2 pos, Vector2 dir, float dist, out bool hit)
        {
            Vector2 center = pos + offset;
            Vector2 box = new Vector2(size.x - Skin * 2f, size.y - Skin * 2f);
            RaycastHit2D[] hits = Physics2D.BoxCastAll(center, box, 0f, dir, dist + Skin, terrainMask);

            float best = dist;
            hit = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D c = hits[i].collider;
                if (c == null || c.isTrigger) continue;
                if (hits[i].distance <= 0f) continue; // already overlapping: let it move out
                float m = Mathf.Max(0f, hits[i].distance - Skin);
                if (m <= best)
                {
                    best = m;
                    hit = true;
                }
            }
            return best;
        }

        bool Touching(Vector2 pos, Vector2 dir)
        {
            bool hit;
            Cast(pos, dir, Skin, out hit);
            return hit;
        }

        // ---------------------------------------------------------------- combat

        void Attack(int aimY)
        {
            attackCooldown = AttackCooldown;
            attackAnimTimer = AttackAnimTime;

            Vector2 p = (Vector2)transform.position + offset;
            Vector2 center;
            Vector2 box;
            float direction;

            if (aimY > 0)
            {
                center = p + new Vector2(0f, 1.3f);
                box = new Vector2(1.4f, 1.8f);
                direction = 90f;
                attackClip = "UpSlash";
            }
            else if (aimY < 0 && !grounded)
            {
                center = p + new Vector2(0f, -1.4f);
                box = new Vector2(1.4f, 1.8f);
                direction = 270f;
                attackClip = "DownSlash";
            }
            else
            {
                float f = facingRight ? 1f : -1f;
                center = p + new Vector2(f * 1.3f, 0f);
                box = new Vector2(2.0f, 1.2f);
                direction = facingRight ? 0f : 180f;
                attackClip = altSlash ? "SlashAlt" : "Slash";
                altSlash = !altSlash;
            }
            currentClip = null; // force the slash animation to restart

            bool downSlash = direction == 270f;
            bool pogo = false;
            hitThisSwing.Clear();

            Collider2D[] cols = Physics2D.OverlapBoxAll(center, box, 0f);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D c = cols[i];
                if (c == null) continue;

                if (downSlash)
                {
                    DamageHero dh = c.GetComponent<DamageHero>();
                    if (dh != null && dh.enabled && dh.hazardType == 2) pogo = true; // spikes
                }

                HealthManager hm = c.GetComponent<HealthManager>();
                if (hm == null) hm = c.GetComponentInParent<HealthManager>();
                if (hm == null || hm.isDead || hitThisSwing.Contains(hm)) continue;
                hitThisSwing.Add(hm);

                Game.HitEnemy(hm, gameObject, NailDamage(), direction);

                if (downSlash) pogo = true;
                RegisterNailHit();
            }

            if (pogo)
            {
                vel.y = PogoSpeed;
                jumping = false;
                usedAirDash = false;
                usedDoubleJump = false;
            }
        }

        void RegisterNailHit()
        {
            int per = LocalCoop.Settings.NailHitsPerHeal;
            if (per <= 0 || masks >= MaxMasks) return;
            hitsTowardHeal++;
            if (hitsTowardHeal >= per)
            {
                hitsTowardHeal = 0;
                masks++;
            }
        }

        void CheckHurt()
        {
            if (invuln > 0f) return;

            Vector2 center = (Vector2)transform.position + offset;
            Collider2D[] cols = Physics2D.OverlapBoxAll(center, size * 0.8f, 0f);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D c = cols[i];
                if (c == null || !c.enabled) continue;
                DamageHero dh = c.GetComponent<DamageHero>();
                if (dh == null || !dh.enabled || dh.damageDealt <= 0) continue;

                TakeDamage(dh.damageDealt, c.bounds.center.x, dh.hazardType > 1);
                return;
            }
        }

        void TakeDamage(int amount, float sourceX, bool hazard)
        {
            masks -= amount;
            invuln = InvulnTime;
            dashTimer = 0f;
            jumping = false;

            if (masks <= 0)
            {
                masks = 0;
                downed = true;
                downedTimer = Mathf.Max(0.5f, LocalCoop.Settings.RespawnSeconds);
                vel = Vector2.zero;
                return;
            }

            if (hazard)
            {
                // Spikes/acid/pits: put Player 2 back next to Player 1.
                SnapToHero();
                return;
            }

            float away = transform.position.x >= sourceX ? 1f : -1f;
            vel = new Vector2(away * 10f, 10f);
            controlLock = KnockbackLock;
        }

        void FullHeal()
        {
            masks = MaxMasks;
            downed = false;
            hitsTowardHeal = 0;
        }

        // ---------------------------------------------------------------- camera leash

        void Leash(float dt)
        {
            Vector3 heroPos = hero.transform.position;
            if (transform.position.y < heroPos.y - 30f)
            {
                SnapToHero();
                return;
            }

            bool far;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(transform.position);
                far = vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.1f || vp.y > 1.1f;
            }
            else
            {
                far = Vector2.Distance(transform.position, heroPos) > 15f;
            }

            offscreenTimer = far ? offscreenTimer + dt : 0f;
            if (offscreenTimer > LocalCoop.Settings.LeashSeconds) SnapToHero();
        }

        void SnapToHero()
        {
            Vector3 h = hero.transform.position;
            z = h.z + DepthOffset;
            transform.position = new Vector3(h.x, h.y, z);
            vel = Vector2.zero;
            offscreenTimer = 0f;
            dashTimer = 0f;
            jumping = false;
            controlLock = 0f;
            wallJumpLock = 0f;
        }

        // ---------------------------------------------------------------- visuals

        void SetHidden(bool h)
        {
            hidden = h;
            if (meshRenderer != null) meshRenderer.enabled = !h;
        }

        void UpdateVisuals()
        {
            // The Knight's art faces left; the game flips X to face right.
            transform.localScale = new Vector3(facingRight ? -1f : 1f, 1f, 1f);

            Color c = tint;
            if (downed) c.a = 0.3f;
            sprite.color = c;

            bool flicker = invuln > 0f && !downed && Mathf.Repeat(Time.time, 0.2f) < 0.1f;
            meshRenderer.enabled = !flicker;

            string clip;
            if (downed) clip = "Idle";
            else if (attackAnimTimer > 0f) clip = attackClip;
            else if (dashTimer > 0f) clip = "Dash";
            else if (wallSliding) clip = "Wall Slide";
            else if (!grounded) clip = "Airborne";
            else if (Mathf.Abs(vel.x) > 0.1f) clip = "Run";
            else clip = "Idle";
            Play(clip);
        }

        void Play(string clip)
        {
            if (clip == currentClip) return;
            if (anim.GetClipByName(clip) == null)
            {
                if (clip == "Idle") return;
                clip = "Idle";
                if (clip == currentClip || anim.GetClipByName(clip) == null) return;
            }
            currentClip = clip;
            anim.Play(clip);
        }

        void OnGUI()
        {
            if (hidden || hero == null) return;

            const float s = 20f, pad = 6f;
            int max = MaxMasks;
            float x0 = Screen.width - max * (s + pad) - 20f;
            float y0 = 20f;

            Color old = GUI.color;
            GUI.color = Color.white;
            GUI.Label(new Rect(x0 - 32f, y0, 32f, s + 4f), "P2");
            for (int i = 0; i < max; i++)
            {
                GUI.color = i < masks ? new Color(tint.r, tint.g, tint.b, 1f) : new Color(1f, 1f, 1f, 0.2f);
                GUI.DrawTexture(new Rect(x0 + i * (s + pad), y0, s, s), Texture2D.whiteTexture);
            }
            if (downed)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(x0, y0 + s + 4f, 220f, 24f),
                    "Back in " + Mathf.CeilToInt(Mathf.Max(0f, downedTimer)) + "...");
            }
            GUI.color = old;
        }

        // ---------------------------------------------------------------- helpers

        float HeroFloat(string field, float fallback)
        {
            object v = Game.Get(hero, field);
            if (v is float && (float)v > 0f) return (float)v;
            return fallback;
        }

        static bool HasAbility(string field)
        {
            return PlayerData.instance != null && PlayerData.instance.GetBool(field);
        }

        static int NailDamage()
        {
            if (PlayerData.instance == null) return 5;
            return Mathf.Max(1, PlayerData.instance.GetInt("nailDamage"));
        }
    }
}
