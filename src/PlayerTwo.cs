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
        const int SoulMax = 99;
        const int SoulPerHit = 11;
        const int FocusCost = 33;
        const float FocusTime = 0.9f;
        // Draw just behind the Knight but in front of the scenery and fog behind him.
        const float DepthOffset = 0.001f;

        public InputDevice Device;

        // Copied from the Knight's own HeroController values in Init (these are fallbacks).
        float RunSpeed = 8.3f;
        float JumpSpeed = 16.65f;
        float MaxFallSpeed = 20.9f;
        float DashSpeed = 20f;
        float WalkSpeed = 6f;

        // The Knight's real ground speed, measured while he runs. Covers walk zones,
        // Sprintmaster and anything else that changes how fast he moves.
        Rigidbody2D heroBody;
        float observedRun;
        float loggedRun;

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
        int soul;
        bool focusing;
        float focusTimer;
        readonly Dictionary<GameObject, float> recentGeo = new Dictionary<GameObject, float>();
        readonly EnemyTargeting targeting = new EnemyTargeting();
        readonly Interactions interactions = new Interactions();
        readonly P2Hud hud = new P2Hud();
        bool p2Sitting;
        float hurtTimer;
        float focusGetTimer;
        float healFlash;
        GameObject focusFx;
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

        // The white slash arcs, copied from the Knight's own Attacks/* children.
        class SlashEffect
        {
            public Transform Source;
            public GameObject Go;
            public tk2dSprite Sprite;
            public tk2dSpriteAnimator Anim;
            public MeshRenderer Renderer;
            public string Clip;
        }
        readonly Dictionary<string, SlashEffect> slashes = new Dictionary<string, SlashEffect>();
        SlashEffect activeSlash;
        float slashTimer;
        const float SlashEffectTime = 0.25f;

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
            WalkSpeed = HeroFloat("WALK_SPEED", WalkSpeed);
            heroBody = hero.GetComponent<Rigidbody2D>();
            LocalCoop.Info("Player 2 speeds: run " + RunSpeed + ", jump " + JumpSpeed +
                           ", fall " + MaxFallSpeed + ", dash " + DashSpeed);

            foreach (string name in new[] { "Slash", "AltSlash", "UpSlash", "DownSlash" })
            {
                SlashEffect fx = CreateSlashEffect(name);
                if (fx != null) slashes[name] = fx;
            }
            LocalCoop.Info("Player 2 slash effects found: " + slashes.Count + "/4");
            focusFx = CloneHeroEffect("Focus Effects");

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
                hud.Update(masks, MaxMasks, tint, false, dt);
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
                FullHeal(); // resting at a bench heals both players
            }

            if (p2Sitting)
            {
                // Player 2 sitting on a bench on their own: heal, stand up on any input.
                FullHeal();
                vel = Vector2.zero;
                if (Device != null && (Pressed("jump") || Pressed("attack") || Pressed("dash") ||
                                       Pressed("left") || Pressed("right") || Pressed("down")))
                {
                    p2Sitting = false;
                    currentClip = null;
                }
                UpdateVisuals();
                hud.Update(masks, MaxMasks, tint, true, dt);
                return;
            }

            Tick(dt);
            ObserveHeroSpeed();
            RefreshBindings(dt);
            UpdateSlash(dt);

            if (downed)
            {
                targeting.Update(gameObject, hero, false, Vector2.zero, dt);
                interactions.ReleaseAll(hero);
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
                CollectGeo();
                targeting.Update(gameObject, hero, true, (Vector2)transform.position + offset, dt);
                interactions.Update((Vector2)transform.position + offset, hero, dt);
                CheckHurt();
                Leash(dt);
            }

            UpdateVisuals();
            hud.Update(masks, MaxMasks, tint, true, dt);
        }

        void OnDestroy()
        {
            hud.Clear();
            interactions.ReleaseAll(hero);
            if (hero != null) targeting.Restore(gameObject, hero);
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
            hurtTimer -= dt;
            focusGetTimer -= dt;
            healFlash -= dt;
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

            // Up at a prompt: pass the press on to the game for a moment.
            if (Device != null && interactions.P2InRange && Pressed("up") && grounded)
            {
                Transform bench = interactions.BenchInRange();
                if (bench != null)
                {
                    // Player 2 sits on the bench themselves; the Knight isn't moved.
                    p2Sitting = true;
                    facingRight = hero.cState.facingRight;
                    Vector3 bp = bench.position;
                    transform.position = new Vector3(bp.x, transform.position.y, z);
                    vel = Vector2.zero;
                    focusing = false;
                    currentClip = null;
                    interactions.ReleaseAll(hero);
                    LocalCoop.Info("Player 2 sat on '" + bench.name + "' at " + bp + " (Player 2 at " + transform.position + ")");
                    return;
                }
                CoopManager.LendP1Device = Device;
                CoopManager.LendUntil = Time.time + 0.25f;
            }

            // Focus: hold to spend soul and heal, standing still on the ground.
            bool focusHeld = Device != null && Held("focus");
            if (focusing)
            {
                if (!focusHeld || !grounded || controlLock > 0f)
                {
                    focusing = false;
                }
                else
                {
                    vel.x = 0f;
                    focusTimer -= Time.deltaTime;
                    if (focusTimer <= 0f)
                    {
                        masks = Mathf.Min(MaxMasks, masks + 1);
                        soul -= FocusCost;
                        currentClip = null;
                        focusGetTimer = 0.35f;
                        healFlash = 0.2f;
                        if (soul >= FocusCost && masks < MaxMasks) focusTimer = FocusTime;
                        else focusing = false;
                    }
                    lastMoveX = 0;
                    lastJumpDown = false;
                    return;
                }
            }
            else if (focusHeld && grounded && soul >= FocusCost && masks < MaxMasks
                     && dashTimer <= 0f && attackAnimTimer <= 0f && controlLock <= 0f)
            {
                focusing = true;
                focusTimer = FocusTime;
                vel.x = 0f;
                lastMoveX = 0;
                lastJumpDown = false;
                return;
            }

            // Horizontal movement
            if (dashTimer > 0f)
            {
                vel.x = (facingRight ? 1f : -1f) * DashSpeed;
                vel.y = 0f;
            }
            else if (controlLock <= 0f && wallJumpLock <= 0f)
            {
                vel.x = moveX * CurrentRunSpeed();
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
        static readonly string[] BoundActions = { "left", "right", "up", "down", "jump", "attack", "dash", "focus" };
        static readonly Dictionary<string, string[]> DefaultBindings = new Dictionary<string, string[]>
        {
            { "left", new[] { "LeftStickLeft", "DPadLeft" } },
            { "right", new[] { "LeftStickRight", "DPadRight" } },
            { "up", new[] { "LeftStickUp", "DPadUp" } },
            { "down", new[] { "LeftStickDown", "DPadDown" } },
            { "jump", new[] { "Action1" } },
            { "attack", new[] { "Action3" } },
            { "dash", new[] { "RightTrigger" } },
            { "focus", new[] { "Action2" } },
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
                if (list.Count == 0 && action == "focus") list = Game.BoundControls("cast");
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
            ShowSlash(attackClip);

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
            soul = Mathf.Min(SoulMax, soul + SoulPerHit);
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
            focusing = false;
            SpawnHitEffect(amount > 1);
            hurtTimer = 0.3f;
            currentClip = null;
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

        /// <summary>The Knight's own "took a hit" burst (the black cloud), at Player 2.</summary>
        void SpawnHitEffect(bool doubleHit)
        {
            try
            {
                var prefab = Game.Get(hero, doubleHit ? "takeHitDoublePrefab" : "takeHitPrefab") as GameObject;
                if (prefab == null) prefab = Game.Get(hero, "takeHitPrefab") as GameObject;
                if (prefab == null) return;
                Vector3 p = transform.position;
                GameObject fx = UnityEngine.Object.Instantiate(prefab, new Vector3(p.x, p.y, prefab.transform.position.z), Quaternion.identity);
                fx.SetActive(true);
                UnityEngine.Object.Destroy(fx, 3f);
            }
            catch (System.Exception e)
            {
                LocalCoop.Info("Player 2 hit effect error: " + e.Message);
            }
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

        void CollectGeo()
        {
            Vector2 center = (Vector2)transform.position + offset;
            Collider2D[] cols = Physics2D.OverlapBoxAll(center, size * 1.2f, 0f);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D c = cols[i];
                if (c == null) continue;
                Component geo = c.GetComponent("GeoControl");
                if (geo == null) continue;
                GameObject g = geo.gameObject;
                if (!g.activeInHierarchy) continue;
                float t;
                if (recentGeo.TryGetValue(g, out t) && Time.time - t < 1f) continue;
                recentGeo[g] = Time.time;
                Game.CollectGeo(geo, hero);
            }
            if (recentGeo.Count > 64) recentGeo.Clear();
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
            if (h)
            {
                p2Sitting = false;
                HideSlash();
                if (hero != null) targeting.Restore(gameObject, hero);
                interactions.ReleaseAll(hero);
            }
        }

        void UpdateVisuals()
        {
            // The Knight's art faces left; the game flips X to face right.
            transform.localScale = new Vector3(facingRight ? -1f : 1f, 1f, 1f);

            Color c = tint;
            if (downed) c.a = 0.3f;
            if (focusing)
            {
                float progress = 1f - Mathf.Max(0f, focusTimer) / FocusTime;
                float pulse = 0.25f + 0.35f * progress;
                c = new Color(Mathf.Lerp(c.r, 1f, pulse), Mathf.Lerp(c.g, 1f, pulse), Mathf.Lerp(c.b, 1f, pulse), c.a);
            }
            if (healFlash > 0f) c = new Color(1f, 1f, 1f, c.a);
            sprite.color = c;

            if (focusFx != null && focusFx.activeSelf != focusing) focusFx.SetActive(focusing);

            bool flicker = invuln > 0f && !downed && Mathf.Repeat(Time.time, 0.2f) < 0.1f;
            meshRenderer.enabled = !flicker;

            string clip;
            if (p2Sitting) clip = SitClip();
            else if (downed) clip = "Idle";
            else if (hurtTimer > 0f) clip = "Recoil";
            else if (focusGetTimer > 0f) clip = FirstClip("Focus Get Once", "Focus Get", "Focus");
            else if (focusing) clip = "Focus";
            else if (attackAnimTimer > 0f) clip = attackClip;
            else if (dashTimer > 0f) clip = "Dash";
            else if (wallSliding) clip = "Wall Slide";
            else if (!grounded) clip = "Airborne";
            else if (Mathf.Abs(vel.x) > 0.1f) clip = "Run";
            else clip = "Idle";
            Play(clip);
        }

        string sitClip;

        string SitClip()
        {
            if (sitClip == null)
            {
                sitClip = "Idle";
                foreach (string n in new[] { "Sitting", "Sit Idle", "Sit" })
                {
                    if (anim.GetClipByName(n) != null) { sitClip = n; break; }
                }
            }
            return sitClip;
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
            Rect bar;
            if (hud.SoulBarRect(max, out bar))
            {
                // The copied HUD shows masks and the vessel; add soul and the respawn timer.
                GUI.color = new Color(1f, 1f, 1f, 0.2f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = focusing ? Color.white : new Color(0.9f, 0.9f, 0.9f, 0.9f);
                float w = bar.width * soul / (float)SoulMax;
                GUI.DrawTexture(new Rect(bar.x + bar.width - w, bar.y, w, bar.height), Texture2D.whiteTexture);
                if (downed)
                {
                    GUI.color = Color.white;
                    GUI.Label(new Rect(bar.x, bar.y + 10f, 220f, 24f), "Back in " + Mathf.CeilToInt(Mathf.Max(0f, downedTimer)) + "...");
                }
                GUI.color = old;
                return;
            }

            GUI.color = Color.white;
            GUI.Label(new Rect(x0 - 32f, y0, 32f, s + 4f), "P2");
            for (int i = 0; i < max; i++)
            {
                GUI.color = i < masks ? new Color(tint.r, tint.g, tint.b, 1f) : new Color(1f, 1f, 1f, 0.2f);
                GUI.DrawTexture(new Rect(x0 + i * (s + pad), y0, s, s), Texture2D.whiteTexture);
            }
            // Soul meter
            float barW = max * (s + pad) - pad;
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(new Rect(x0, y0 + s + 4f, barW, 6f), Texture2D.whiteTexture);
            GUI.color = focusing ? new Color(1f, 1f, 1f, 1f) : new Color(0.9f, 0.9f, 0.9f, 0.9f);
            GUI.DrawTexture(new Rect(x0, y0 + s + 4f, barW * soul / (float)SoulMax, 6f), Texture2D.whiteTexture);

            if (downed)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(x0, y0 + s + 14f, 220f, 24f),
                    "Back in " + Mathf.CeilToInt(Mathf.Max(0f, downedTimer)) + "...");
            }
            GUI.color = old;
        }

        // ---------------------------------------------------------------- helpers

        SlashEffect CreateSlashEffect(string name)
        {
            Transform src = hero.transform.Find("Attacks/" + name);
            if (src == null) return null;
            var srcSprite = src.GetComponent<tk2dSprite>();
            var srcAnim = src.GetComponent<tk2dSpriteAnimator>();
            if (srcSprite == null || srcAnim == null) return null;

            // NailSlash.animName is the clip the game plays for this swing.
            string clip = Game.Get(src.GetComponent("NailSlash"), "animName") as string;
            if (string.IsNullOrEmpty(clip) || srcAnim.GetClipByName(clip) == null) return null;

            var go = new GameObject("P2 " + name);
            go.transform.parent = transform;
            var fx = new SlashEffect
            {
                Source = src,
                Go = go,
                Sprite = tk2dSprite.AddComponent(go, srcSprite.Collection, srcSprite.spriteId),
                Anim = go.AddComponent<tk2dSpriteAnimator>(),
                Renderer = go.GetComponent<MeshRenderer>(),
                Clip = clip
            };
            fx.Anim.Library = srcAnim.Library;

            var srcRenderer = src.GetComponent<MeshRenderer>();
            if (fx.Renderer != null)
            {
                if (srcRenderer != null)
                {
                    fx.Renderer.sortingLayerID = srcRenderer.sortingLayerID;
                    fx.Renderer.sortingOrder = srcRenderer.sortingOrder;
                }
                fx.Renderer.enabled = false;
            }
            return fx;
        }

        string FirstClip(params string[] names)
        {
            foreach (string n in names)
                if (anim.GetClipByName(n) != null) return n;
            return "Idle";
        }

        /// <summary>
        /// A visual-only copy of one of the Knight's effect children (scripts removed),
        /// attached to Player 2 and hidden until used. Logs what's inside for tuning.
        /// </summary>
        GameObject CloneHeroEffect(string childName)
        {
            try
            {
                Transform src = hero.transform.Find(childName);
                if (src == null)
                {
                    LocalCoop.Info("Player 2 effect '" + childName + "' not found on the Knight.");
                    return null;
                }
                var holder = new GameObject("LocalCoop holder");
                holder.SetActive(false);
                GameObject c = UnityEngine.Object.Instantiate(src.gameObject, holder.transform);
                var clips = new List<string>();
                foreach (MonoBehaviour mb in c.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    string n = mb.GetType().Name;
                    if (n.StartsWith("tk2d"))
                    {
                        var an = mb as tk2dSpriteAnimator;
                        if (an != null)
                        {
                            var arr = Game.Get(an.Library, "clips") as System.Array;
                            if (arr != null)
                                foreach (object clip in arr)
                                    clips.Add(mb.gameObject.name + ":" + (Game.Get(clip, "name") as string));
                        }
                        continue;
                    }
                    if (n.StartsWith("ParticleSystem")) continue;
                    UnityEngine.Object.DestroyImmediate(mb);
                }
                c.transform.parent = transform;
                c.transform.localPosition = src.localPosition;
                c.transform.localRotation = src.localRotation;
                c.transform.localScale = src.localScale;
                UnityEngine.Object.Destroy(holder);
                c.SetActive(false);
                LocalCoop.Info("Player 2 effect '" + childName + "' copied; animations: " + string.Join(", ", clips.ToArray()));
                return c;
            }
            catch (System.Exception e)
            {
                LocalCoop.Info("Player 2 effect '" + childName + "' error: " + e.Message);
                return null;
            }
        }

        void ShowSlash(string clip)
        {
            HideSlash();
            SlashEffect fx;
            if (!slashes.TryGetValue(clip == "SlashAlt" ? "AltSlash" : clip, out fx) || fx.Renderer == null) return;

            // Follow the Knight's own placement/size (charms like Longnail change it).
            fx.Go.transform.localPosition = fx.Source.localPosition;
            fx.Go.transform.localRotation = fx.Source.localRotation;
            fx.Go.transform.localScale = fx.Source.localScale;
            fx.Sprite.color = new Color(Mathf.Lerp(1f, tint.r, 0.5f), Mathf.Lerp(1f, tint.g, 0.5f), Mathf.Lerp(1f, tint.b, 0.5f), 1f);
            fx.Renderer.enabled = true;
            fx.Anim.Play(fx.Clip);
            activeSlash = fx;
            slashTimer = SlashEffectTime;
        }

        void UpdateSlash(float dt)
        {
            if (activeSlash == null) return;
            slashTimer -= dt;
            if (slashTimer <= 0f) HideSlash();
        }

        void HideSlash()
        {
            if (activeSlash != null && activeSlash.Renderer != null) activeSlash.Renderer.enabled = false;
            activeSlash = null;
        }

        void ObserveHeroSpeed()
        {
            if (heroBody == null) return;
            HeroControllerStates st = hero.cState;
            float vx = Mathf.Abs(heroBody.velocity.x);
            if (st.onGround && !st.dashing && vx > 1f && vx < RunSpeed * 1.6f)
            {
                observedRun = vx;
                if (Mathf.Abs(observedRun - loggedRun) > 0.2f)
                {
                    loggedRun = observedRun;
                    LocalCoop.Info("Knight's run speed is " + observedRun + "; Player 2 matched.");
                }
            }
        }

        float CurrentRunSpeed()
        {
            if (hero.cState.inWalkZone) return WalkSpeed;
            return observedRun > 0f ? observedRun : RunSpeed;
        }

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
