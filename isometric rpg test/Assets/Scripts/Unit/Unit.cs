using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEngine.EventSystems;
using Unity.VisualScripting;
using UnityEngine.UIElements.Experimental;

public class Unit : MonoBehaviour, IClickable
{
    // UnitClicked event is invoked when user clicks the unit. 
    public event EventHandler UnitClicked;

    // UnitSelected event is invoked when user clicks on unit that belongs to him. 
    public event EventHandler UnitSelected;

    // UnitDeselected event is invoked when user click outside of currently selected unit's collider.
    public event EventHandler UnitDeselected;
 
    // UnitHighlighted event is invoked when user moves cursor over the unit. 
    public event EventHandler UnitHighlighted;
       
    // UnitDehighlighted event is invoked when cursor exits unit's collider. 
    public event EventHandler UnitDehighlighted;

    // UnitDestroyed event is invoked when unit's hitpoints drop below 0.
    public event EventHandler<AttackEventArgs> UnitDestroyed;

    // UnitMoved event is invoked when unit moves from one tile to another.
    public event EventHandler<MovementEventArgs> UnitMoved;

    public UnitState UnitState { get; set; }
    public void SetState(UnitState state)
    {
        UnitState.TransitionState(state);
    }

    public int TotalHitPoints { get; private set; }
    public int TotalMovementPoints { get; private set; }
    public int TotalActionPoints { get; private set; }

    public OverlayTile Tile;

    private Animator Anim;

    public int HitPoints;
    public int AttackRange;
    public int AttackFactor;
    public int DefenceFactor;
    public int SkillFactor;
    public int LuckFactor;
    public int CritFactor = 100;
    public float MovementAnimationSpeed = 7f;
    public string UnitName;
    public Sprite UnitPortrait;
    public Sprite UnitBattleSprite;
    public RuntimeAnimatorController BattleAnimController;

    //BattleSystem 
    public GameObject HitEffect;
    public GameObject CritEffect;

    [HideInInspector]
    public List<OverlayTile> cachedPath;

    [SerializeField]
    private int movementPoints;
    public int MovementPoints
    {
        get { return movementPoints; }
        set { movementPoints = value; }
    }

    [SerializeField]
    private int actionPoints = 1;
    public int ActionPoints
    {
        get { return actionPoints; }
        set { actionPoints = value; }
    }

    public int PlayerNumber;
    public bool IsMoving { get; set; }

    private AStarPathfinder _pathfinder = new AStarPathfinder();
    private RangeFinder rangeFinder;

    //Initializes the unit. Called whenever a unit gets added into the game
    public virtual void Initialize()
    {      
        UnitState = new UnitStateNormal(this);
        MovementAnimationSpeed = 7f;

        TotalHitPoints = HitPoints;
        TotalMovementPoints = MovementPoints;
        TotalActionPoints = ActionPoints;

        Anim = GetComponent<Animator>();
        rangeFinder = new RangeFinder();

        Tile = GetStartingTile();
        Tile.IsBlocked = true;
        Tile.CurrentUnit = this;

        cachedPath = new List<OverlayTile>();
        
    }

    private OverlayTile GetStartingTile()
    {
        var tileGrid = FindObjectOfType<TileGrid>();
        var tileMap = tileGrid.Tilemap;

        var tilePos = new Vector2Int(tileMap.WorldToCell(transform.position).x, tileMap.WorldToCell(transform.position).y);

        if (tileGrid.Map.TryGetValue(tilePos, out OverlayTile tile))
        {
            return tile;
        }
        return null;
    }
        
    public void OnPointerDown()
    {
        if (UnitClicked != null)
            UnitClicked.Invoke(this, EventArgs.Empty);       
    }

    public void OnMouseEnter()
    {       
        if (UnitHighlighted != null)
            UnitHighlighted.Invoke(this, EventArgs.Empty);
        
    }
    public void OnMouseExit()
    {       
        if (UnitDehighlighted != null)
            UnitDehighlighted.Invoke(this, EventArgs.Empty);
        
    }

    //Called at the start of each turn
    public virtual void OnTurnStart()
    {
        cachedPath = new List<OverlayTile>();
        Anim.SetBool("IsFinished", false);

    }

    //Called on the end of each turn
    public virtual void OnTurnEnd()
    {
        if (HitPoints > 0)
        {
            MovementPoints = TotalMovementPoints;
            ActionPoints = TotalActionPoints;

            SetState(new UnitStateNormal(this));
        }
        else
        {
            OnDestroyed();
        }
    }
        
    //Called when Unit is dead
    protected virtual void OnDestroyed()
    {
        TotalMovementPoints = 0;
        TotalActionPoints = 0;
        Tile.IsBlocked = false;
        Tile.CurrentUnit = null;
        SetState(new UnitStateDestroyed(this));
            
    }

    //Called when unit is selected
    public virtual void OnUnitSelected()
    {
        if (FindObjectOfType<TileGrid>().GetCurrentPlayerUnits().Contains(this))
        {
            SetState(new UnitStateSelected(this));
        }
        if (UnitSelected != null)
        {
            UnitSelected.Invoke(this, EventArgs.Empty);
        }
    }

    //Called when unit is deselected
    public virtual void OnUnitDeselected()
    {
        if (FindObjectOfType<TileGrid>().GetCurrentPlayerUnits().Contains(this))
        {
            SetState(new UnitStateFriendly(this));
        }
        if (UnitDeselected != null)
        {
            UnitDeselected.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Calculates whether the unit is attackable
    /// </summary>
    /// <param name="otherUnit">Unit to attack</param>
    /// <param name="tile">Tile to perform an attack from</param>
    /// <returns>A boolean that determines if otherUnit is attackable </returns>
    public virtual bool IsUnitAttackable(Unit otherUnit)
    {
        return FindObjectOfType<TileGrid>().GetManhattenDistance(Tile, otherUnit.Tile) <= AttackRange
            && otherUnit.PlayerNumber != PlayerNumber
            && ActionPoints >= 1
            && otherUnit.HitPoints > 0;
    }

    /// <summary>
    /// Handles the attack event against the other unit
    /// </summary>
    /// <param name="unitToAttack"></param>
    public virtual DamageDetails AttackHandler(Unit unitToAttack, bool isCounterAttacker)
    {
        AttackAction attackAction = GetAttackAndCost(unitToAttack, isCounterAttacker);
        ActionPoints -= attackAction.ActionCost;
        if (!attackAction.InRange)
        {
            Debug.Log("Damge Details false");
            return new DamageDetails(false);
        }
        else
        {
            Debug.Log("Damge Details true");
            return unitToAttack.DefendHandler(this, attackAction);
        }              
    }

    public virtual void ReceiveDamage(int dmg)
    {
        HitPoints -= dmg;
    }

    /// <summary>
    /// Gets damage given to the other unit and action point cost of the action
    /// </summary>
    /// <param name="unitToAttack"> The unit under attack </param>
    /// <returns>An AttackAction that contains the damage given and action cost taken </returns>
    protected virtual AttackAction GetAttackAndCost(Unit unitToAttack, bool isCounterAttacker)
    {
        //counterattacker will not use action points
        if (isCounterAttacker)
        {
            //if the counter attacking unit can match the enemy range
            if (IsUnitAttackable(unitToAttack))
                return new AttackAction(true, 0);
            else
                return new AttackAction(false, 0);
        }

        return new AttackAction(true, 1);
    }

    /// <summary>
    /// Handles the defend event of the unit being attacked
    /// </summary>
    /// <param name="aggressor"> Unit that is attacking </param>
    /// <param name="damage"> Damage given by the other unit </param>
    public DamageDetails DefendHandler(Unit aggressor, AttackAction attackAction)
    {       
        var damageDetails = CalculateDamageDetails(aggressor, attackAction);
        HitPoints -= damageDetails.TotalDamage;
            
        if (HitPoints <= 0)
        {
            HitPoints = 0;
            damageDetails.IsDead = true;
            if (UnitDestroyed != null)
            {
                UnitDestroyed.Invoke(this, new AttackEventArgs(aggressor, this, damageDetails.TotalDamage));
            }
            OnDestroyed();
        }

        return damageDetails;
    }

    public HealingDetails ReceiveHealing(int healAmount)
    {
        HitPoints = Mathf.Clamp(HitPoints + healAmount, 0, TotalHitPoints);

        return new HealingDetails(healAmount, HitPoints);
    }

    //Gets weapon effectiveness against a unit. -1 for ineffective, 0 for neutral, and 1 for effective
    public virtual int GetEffectiveness(Unit unitToAttack)
    {
        return 0;
    }

    public int GetCritChance()
    {
        //Critical Chance formula = (Skill / 2) + Critical bonus (raw crit bonus, unique for each unit)
        return Mathf.Clamp((SkillFactor / 2) + CritFactor, 0, 100);
    }

    public int GetHitChance()
    {
        //Raw Accuracy formula = (Skill x 2) + (Luck / 2)
        return (SkillFactor * 2) + (LuckFactor / 2) + 50;
    }

    public int GetAttack(){return AttackFactor;}

    public int GetDodgeChance(){return LuckFactor + Tile.AvoidBoost;}

    public int GetDefense(){return DefenceFactor + Tile.DefenseBoost;}

    public virtual int GetBattleAccuracy(Unit unitToAttack)
    {
        //Weapon Effectiveness mutiplier for hit chance = 15;
        int weaponEffectiveness = GetEffectiveness(unitToAttack) * 15;

        return Mathf.Clamp((GetHitChance() + weaponEffectiveness) - unitToAttack.GetDodgeChance(), 0, 100);
    }

    public virtual int GetTotalDamage(Unit unitToAttack)
    {
        //Weapon Effectiveness mutiplier = 2;
        int weaponEffectiveness = GetEffectiveness(unitToAttack) * 2;

        return (GetAttack() + weaponEffectiveness) - unitToAttack.GetDefense();
    }

    /// <summary>
    /// Calculates actual damage given
    /// </summary>
    /// <param name="aggressor">Unit that performed the attack</param>
    /// <param name="rawDamage">Raw damage that the attack caused</param>
    /// <returns>Actual damage the unit has taken</returns>        
    protected DamageDetails CalculateDamageDetails(Unit attacker, AttackAction attackerAction)
    {     
        var battleAccuracy = attacker.GetBattleAccuracy(this) - GetDodgeChance();

        //modifiers
        int crit = 1;
        int dodge = 1;
        //check for crit, crits are 2x damage
        if (UnityEngine.Random.value < (attacker.GetCritChance() * 0.01))
        {
            crit = 2;
        }
        //check for hit, dodges negate all damage
        else if (UnityEngine.Random.value > (battleAccuracy * 0.01))
        {           
            dodge = 0;
        }
        var totalDamage = attacker.GetTotalDamage(this) * dodge * crit;

        //calculate damage
        return new DamageDetails(true, false, dodge == 1, crit == 2, totalDamage);
    }

    public void Move(List<OverlayTile> path)
    {
        if (MovementAnimationSpeed > 0 && path.Count > 1)
        {
            cachedPath = path;
            StartCoroutine(MovementAnimation(path));           
        }      
    }

    /// <summary>
    /// Procedurally moves unit along the path
    /// </summary>
    /// <param name="path"> List of tiles the unit will move through </param>
    protected virtual IEnumerator MovementAnimation(List<OverlayTile> path)
    {
        Anim.SetBool("IsMoving", true);

        if (path.Count <= 1)
        {
            yield return null;
        }

        List<OverlayTile> tempPath = path.ToList();

        IsMoving = true;

        while (tempPath.Count > 0 && IsMoving)
        {
            transform.position = Vector2.MoveTowards(transform.position, tempPath[0].transform.position, Time.deltaTime * MovementAnimationSpeed);

            var heading = tempPath[0].transform.position - transform.position;
            var distance = heading.magnitude;

            //this prevents blend tree parameters from ever going to (0,0)
            if((heading / distance).normalized.x != (heading / distance).normalized.y)
            {
                Anim.SetFloat("MoveX", (heading / distance).normalized.x);
                Anim.SetFloat("MoveY", (heading / distance).normalized.y);

            }

            if (Vector2.Distance(transform.position, tempPath[0].transform.position) < Mathf.Epsilon)
            {
                PositionCharacter(tempPath[0]);
                tempPath.RemoveAt(0);
            }

            yield return 0;
        }
        IsMoving = false;      
    }

    public void ConfirmMove()
    {
        if (cachedPath.Count > 0)
        {         
            foreach (var tile in cachedPath)
            {
                MovementPoints -= tile.MovementCost;
            }

            if (UnitMoved != null)
            {
                UnitMoved.Invoke(this, new MovementEventArgs(cachedPath[0], cachedPath[cachedPath.Count-1], cachedPath));
            }

            MovementPoints = 0;
        }      
    }

    public void SetAnimationToIdle()
    {
        Anim.SetBool("IsMoving", false);
        Anim.Play("Idle", 0, FindObjectOfType<AnimationTimer>().GetCurrentCurrentTime());
    }

    //used for whenver we want to start the unit's movement animation
    public void SetMove()
    {
        Anim.SetBool("IsMoving", true);
        if(cachedPath.Count <= 0)
        {
            //start move down animation
            Anim.SetFloat("MoveX", 0);
            Anim.SetFloat("MoveY", -1);
        }        
    }

    public void ResetMove()
    {
        SetState(new UnitStateNormal(this));
      
        if (cachedPath.Count > 0)
        {
            MovementPoints = TotalMovementPoints;
            PositionCharacter(cachedPath[0]);
            cachedPath = new List<OverlayTile>();
        }
    }

    public void SetFinished()
    {
        Anim.SetBool("IsMoving", false);
        Anim.SetBool("IsFinished", true);
        MovementPoints = 0;
        ActionPoints = 0;
    }

    /// <summary>
    /// Positions the unit at the given tile
    /// </summary>
    /// <param name="tile"> Tile to position unit on</param>
    public void PositionCharacter(OverlayTile tile)
    {
        transform.position = tile.transform.position;
        Tile = tile;       
    }

    public bool IsTileMovableTo(OverlayTile tile)
    {
        if(Tile == tile)
        {
            return true;
        }
        return !tile.IsBlocked;
    }

    //Get a list of tiles that the unit can move to
    public List<OverlayTile> GetAvailableDestinations(TileGrid tileGrid)
    {
        return rangeFinder.GetTilesInMoveRange(this, tileGrid, GetTilesInRange(tileGrid, MovementPoints));
    }

    //Get a list of tiles within the unit's range. Doesn't take tile cost into consideration
    public List<OverlayTile> GetTilesInRange(TileGrid tileGrid, int range)
    {
        return rangeFinder.GetTilesInRange(this, tileGrid, range);
    }

    //Get a list of attackable tiles that doesn't include the tiles that a unit can move to
    public List<OverlayTile> GetTilesInAttackRange(List<OverlayTile> availableDestinations, TileGrid tileGrid)
    {
        return rangeFinder.GetTilesInAttackRange(availableDestinations, tileGrid, AttackRange);
    }

    //Find the optimal path from the tile the unit is on currently, to the destination tile
    public List<OverlayTile> FindPath(OverlayTile destination, TileGrid tileGrid)
    {
        return _pathfinder.FindPath(Tile, destination, GetAvailableDestinations(tileGrid), tileGrid);
    }

    //Visual indication that the unit is destroyed
    public virtual void MarkAsDestroyed()
    {
        GetComponent<SpriteRenderer>().color = Color.black;
        Destroy(gameObject);
    }

    //Visual indication that the unit has no more moves this turn
    public virtual void MarkAsFinished()
    {
        GetComponent<SpriteRenderer>().color = Color.gray;
    }

    public virtual void MarkAsEnemy(Player player)
    {
        GetComponent<SpriteRenderer>().color = player.Color;
    }

    //Return the unit back to its original appearance
    public virtual void UnMark()
    {
        GetComponent<SpriteRenderer>().color = Color.white;
    }

}
//End of unit class
    
public class AttackAction
{
    public bool InRange;
    public int ActionCost;

    public AttackAction(bool inRange, int actionCost)
    {
        InRange = inRange;
        ActionCost = actionCost;
    }
}

public class MovementEventArgs : EventArgs
{
    public OverlayTile StartingTile;
    public OverlayTile DestinationTile;
    public List<OverlayTile> Path;

    public MovementEventArgs(OverlayTile startingTile, OverlayTile destinationTile, List<OverlayTile> path)
    {
        StartingTile = startingTile;
        DestinationTile = destinationTile;
        Path = path;
    }
}
public class AttackEventArgs : EventArgs
{
    public Unit Attacker;
    public Unit Defender;

    public int Damage;

    public AttackEventArgs(Unit attacker, Unit defender, int damage)
    {
        Attacker = attacker;
        Defender = defender;

        Damage = damage;
    }
}

public class UnitCreatedEventArgs : EventArgs
{
    public Unit Unit;
    public List<Ability> Abilities;

    public UnitCreatedEventArgs(Unit unit, List<Ability> unitAbilities )
    {
        Unit = unit;
        Abilities = unitAbilities;
    }
}

