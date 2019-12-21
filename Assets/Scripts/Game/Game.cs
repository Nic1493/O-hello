﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Globals;

public class Game : MonoBehaviour
{
    [SerializeField] Camera mainCam;
    [SerializeField] HUD hud;
    //[SerializeField] AudioController audioController;

    [SerializeField] Transform discHolder;
    [SerializeField] GameObject hintDisc;

    bool inputEnabled = true;

    const int BOARD_SIZE = 8;
    GameObject[,] gameBoard = new GameObject[BOARD_SIZE, BOARD_SIZE];

    Vector3Int[] checkDirections = new Vector3Int[]
    {
        new Vector3Int( 0, 1, 0),
        new Vector3Int( 1, 1, 0),
        new Vector3Int( 1, 0, 0),
        new Vector3Int( 1,-1, 0),
        new Vector3Int( 0,-1, 0),
        new Vector3Int(-1,-1, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(-1, 1, 0)
    };

    List<(Vector3Int direction, int flipCount)> validDirections = new List<(Vector3Int, int)>(8);
    List<((int row, int col) coordinate, int totalFlipCount)> validSpaces = new List<((int, int), int)>();

    //used for CPU to prioritize placing discs on these coordinates on harder difficulty settings
    List<(int row, int col)> corners = new List<(int, int)>(4);
    List<(int row, int col)> edges = new List<(int, int)>((BOARD_SIZE - 1) * 4);

    void Awake()
    {
        ResetGameState();
        InitGameBoard();
    }

    void ResetGameState()
    {
        gameOver = false;
        playerTurn = true;
        inputEnabled = true;
        whiteDiscCount = 2;
        blackDiscCount = 2;

        validSpaces.Add(((2, 4), 1));
        validSpaces.Add(((3, 5), 1));
        validSpaces.Add(((4, 2), 1));
        validSpaces.Add(((5, 3), 1));
    }

    void InitGameBoard()
    {
        for (int row = 0; row < BOARD_SIZE; row++)
        {
            for (int col = 0; col < BOARD_SIZE; col++)
            {
                //set elements in gameBoard
                gameBoard[row, col] = discHolder.GetChild(row * BOARD_SIZE + col).gameObject;

#if UNITY_EDITOR
                //rename gameobjects for easier debugging
                gameBoard[row, col].name = string.Format("Disc [{0}][{1}]", row, col);
#endif
                //init. edge and corner coordinates
                if (row == 0 || col == 0 || row == BOARD_SIZE - 1 || col == BOARD_SIZE - 1)
                {
                    if ((row == 0 || row == BOARD_SIZE - 1) && (col == 0 || col == BOARD_SIZE - 1))
                    {
                        corners.Add((row, col));
                    }
                    else
                    {
                        edges.Add((row, col));
                    }
                }

            }
        }
    }

    void Update()
    {
        if (!gameOver && !gamePaused && playerTurn)
        {
            if (inputEnabled)
            {
                GetMouseInput();
            }
            if (hintsEnabled)
            {
                UpdateHints();
            }
        }
    }

    void GetMouseInput()
    {
        //on L. mouse button released, fire raycast at mouse position
        if (Input.GetMouseButtonUp(0))
        {
            Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);

            //if raycast hit game board...
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.GetType() == typeof(MeshCollider))
            {
                (int row, int col) selectedCoordinate = WorldToBoardCoordinates(hit);

                //if disc at selected coordinate is inactive
                if (!gameBoard[selectedCoordinate.row, selectedCoordinate.col].activeInHierarchy)
                {
                    if (validSpaces.Any(item => item.coordinate == selectedCoordinate))
                    {
                        FindValidDirections(playerTurn, selectedCoordinate);
                        StartCoroutine(MakeMove(selectedCoordinate));
                    }
                }
            }
        }
    }

    void FindValidDirections(bool playerTurn, (int row, int col) coordinate)
    {
        validDirections.Clear();

        //call CheckInDirection for all 8 directions in checkDirections[], and store result in validDirections[]
        for (int i = 0; i < checkDirections.Length; i++)
        {
            if (coordinate.row + checkDirections[i].y >= 0 && coordinate.row + checkDirections[i].y < BOARD_SIZE &&
                coordinate.col + checkDirections[i].x >= 0 && coordinate.col + checkDirections[i].x < BOARD_SIZE)
            {
                var (isValid, flipCount) = CheckInDirection(playerTurn, coordinate, checkDirections[i]);
                if (isValid)
                {
                    validDirections.Add((checkDirections[i], flipCount));
                }
            }
        }
    }

    (bool isValid, int flipCount) CheckInDirection(bool playerTurn, (int row, int col) coordinate, Vector3 direction)
    {
        Vector3 rayOrigin = gameBoard[coordinate.row, coordinate.col].transform.position;
        float rayDistance = direction.normalized.magnitude;
        int currentColourLayer = playerTurn ? blackDiscLayer : whiteDiscLayer;
        int flipCount = 0;

        //continuously raycast to check for a disc of opposite colour
        while (Physics.Raycast(rayOrigin, direction, out RaycastHit oppositeColourDisc, rayDistance, ~(1 << currentColourLayer)))
        {
            //update rayOrigin to transform.position of disc that was hit
            rayOrigin = oppositeColourDisc.transform.position;
            flipCount++;

            //if raycast hits another disc...
            if (Physics.Raycast(rayOrigin, direction, out RaycastHit sameColourDisc, rayDistance))
            {
                //check what colour it is; if same colour, 'sandwich' confirmed, return (true, <how many times this loop iterated>); otherwise continue checking
                if (sameColourDisc.transform.gameObject.layer == currentColourLayer)
                {
                    return (true, flipCount);
                }
            }
            //otherwise break and return false (no disc of same colour to 'close the sandwich')
            else
            {
                break;
            }
        }

        return (false, 0);
    }

    IEnumerator MakeMove((int row, int col) coordinate)
    {
        gameBoard[coordinate.row, coordinate.col].SetActive(true);
        FlipDiscs(coordinate);

        //disable input until flip animation finishes
        inputEnabled = false;
        yield return new WaitForSeconds(FLIP_ANIMATION_DURATION);
        inputEnabled = true;

        int turnsPassed = 0;
        PassTurn(ref turnsPassed);
    }

    void FlipDiscs((int row, int col) coordinate)
    {
        //call FlipInDirection() for all items in validDirections[]
        for (int i = 0; i < validDirections.Count; i++)
        {
            FlipInDirection(coordinate, validDirections[i].direction, validDirections[i].flipCount);
        }
    }

    void FlipInDirection((int row, int col) coordinate, Vector3Int direction, int flipLength)
    {
        //start Disc.FlipUponAxis() for all discs that should be flipped
        for (int i = 1; i <= flipLength; i++)
        {
            Vector3 flipAxis = Vector3.Cross(direction, transform.eulerAngles.x == 0 ? Vector3.forward : Vector3.back);
            gameBoard[coordinate.row + (direction.y * i), coordinate.col + (direction.x * i)].GetComponent<Disc>().FlipUponAxis(flipAxis);

            //increment/decrement disc counts accordingly
            //(player always plays as black)
            if (playerTurn)
            {
                blackDiscCount++;
                whiteDiscCount--;
            }
            else
            {
                blackDiscCount--;
                whiteDiscCount++;
            }
        }
    }

    void PassTurn(ref int _turnsPassed)
    {
        int turnsPassed = _turnsPassed;

        if (turnsPassed < 2)
        {
            //flip all inactive discs
            foreach (var disc in gameBoard)
            {
                if (!disc.activeInHierarchy)
                {
                    disc.GetComponent<Disc>().FlipUponAxis(Vector3.right);
                }
            }

            playerTurn = !playerTurn;
            FindValidSpaces(playerTurn);

            //if a valid move exists...
            if (validSpaces.Count > 0)
            {
                turnsPassed = 0;

                if (!playerTurn)
                {
                    hintDisc.SetActive(false);
                    StartCoroutine(RunCPU());
                }
            }
            //if a valid move doesn't exist, increment number of turns passed and call this function again
            else
            {
                turnsPassed++;
                PassTurn(ref turnsPassed);
            }
        }
        //game is over when turn has been passed twice without a move being made
        //(checking if the board is full is not good enough, because there exists board states in which the board isn't filled and neither player can make a move)
        else
        {
            gameOver = true;
        }

        hud.UpdateHUD();
    }

    //check all coordinates of inactive discs to see if a move can be made there, given whose turn it is
    void FindValidSpaces(bool playerTurn)
    {
        validSpaces.Clear();

        for (int row = 0; row < BOARD_SIZE; row++)
        {
            for (int col = 0; col < BOARD_SIZE; col++)
            {
                if (!gameBoard[row, col].activeInHierarchy)
                {
                    FindValidDirections(playerTurn, (row, col));

                    //if coordinate makes valid move, add to validSpaces[0], with total number of discs flipped
                    if (validDirections.Count > 0)
                    {
                        int totalFlipCount = validDirections.Sum(i => i.flipCount);
                        validSpaces.Add(((row, col), totalFlipCount));
                    }
                }
            }
        }
    }

    IEnumerator RunCPU()
    {
        SortValidMoves();
        (int row, int col) selectedCoordinate = FindCPUMove(cpuDifficulty);

        //wait for a longer time the more discs are on the game board (to add a bit of R E A L I S M)
        float cpuDelay = 1 + (whiteDiscCount + blackDiscCount - 4) / 60f;
        yield return new WaitForSeconds(cpuDelay);

        FindValidDirections(playerTurn, selectedCoordinate);
        StartCoroutine(MakeMove(selectedCoordinate));
    }

    //order validSpaces[] by whether or not item appears in corners[], then by whether or not item appears in edges[], then by item's total number of discs to flip
    void SortValidMoves()
    {
        DebugValidSpaces();
        validSpaces = validSpaces.OrderBy(i => corners.Contains(i.coordinate))
                                  .ThenBy(i => edges.Contains(i.coordinate))
                                  .ThenBy(i => i.totalFlipCount).ToList();
        DebugValidSpaces();
    }

    (int row, int col) FindCPUMove(CPUDifficulty difficulty)
    {
        switch (difficulty)
        {
            case CPUDifficulty.Easy:
                //randomize move choice between all moves that flips fewest discs
                var possibleMoves = validSpaces.TakeWhile(i => i.totalFlipCount == validSpaces[0].totalFlipCount);

                //if (number of worst possible moves) is greater than (number of valid spaces / number of CPU difficulties)...
                if (possibleMoves.Count() > validSpaces.Count / cpuDifficultyCount)
                {
                    //return random selection from possibleMoves
                    return possibleMoves.ElementAt(Random.Range(0, possibleMoves.Count())).coordinate;
                }
                //otherwise return random selection from first "portion" of validSpaces
                //a portion is defined by (1 / number of CPU difficulties)
                else
                {
                    return validSpaces[Random.Range(validSpaces.Count * (int)difficulty / cpuDifficultyCount, validSpaces.Count * ((int)difficulty + 1) / cpuDifficultyCount)].coordinate;
                }

            case CPUDifficulty.Normal:
                //return random selection from second portion of validSpaces
                return validSpaces[Random.Range(validSpaces.Count * (int)difficulty / cpuDifficultyCount, validSpaces.Count * ((int)difficulty + 1) / cpuDifficultyCount)].coordinate;

            case CPUDifficulty.Hard:
                //return random selection from third portion of validSpaces
                return validSpaces[Random.Range(validSpaces.Count * (int)difficulty / cpuDifficultyCount, validSpaces.Count * ((int)difficulty + 1) / cpuDifficultyCount)].coordinate;

            default:
                //this should never happen
                return (-1, -1);
        }
    }

    void UpdateHints()
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.GetType() == typeof(MeshCollider))
        {
            (int row, int col) coordinate = WorldToBoardCoordinates(hit);

            if (validSpaces.Any(i => i.coordinate == coordinate))
            {
                //set hintDisc gameObject to active and position it at coordinate's transform.position
                hintDisc.transform.localPosition = gameBoard[coordinate.row, coordinate.col].transform.position;
                hintDisc.SetActive(true);
            }
            else
            {
                hintDisc.SetActive(false);
            }
        }
    }

    //return row-column coordinates from [0, BOARD_SIZE - 1] based on (hit.point.xy / hit.collider.bounds.extents.xy)
    //bottom-left: (0, 0); top-right: (BOARD_SIZE - 1, BOARD_SIZE - 1)
    (int row, int col) WorldToBoardCoordinates(RaycastHit hit)
    {
        int row = Mathf.FloorToInt(hit.point.y / hit.collider.bounds.extents.y * (BOARD_SIZE / 2) + (BOARD_SIZE / 2));
        int col = Mathf.FloorToInt(hit.point.x / hit.collider.bounds.extents.x * (BOARD_SIZE / 2) + (BOARD_SIZE / 2));
        return (row, col);
    }

    public void BackToMainMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(0);
    }

    #region DEBUG
#if UNITY_EDITOR

    void DebugKeys()
    {
        if (Input.GetKeyDown(KeyCode.Q)) UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        if (Input.GetKeyDown(KeyCode.R)) UnityEngine.SceneManagement.SceneManager.LoadScene(1);
    }

    void DebugBoard()
    {
        for (int row = BOARD_SIZE - 1; row >= 0; row--)
        {
            string output = "";

            for (int col = 0; col < BOARD_SIZE; col++)
            {
                output += gameBoard[row, col].activeInHierarchy ? (gameBoard[row, col].layer == whiteDiscLayer ? "W, " : "B, ") : "o, ";
            }

            print(output);
        }
    }

    void DebugValidDirections((int, int) coordinate)
    {
        print("valid directions for " + coordinate + ": ");

        for (int i = 0; i < validDirections.Count; i++)
        {
            print(checkDirections[i] + " -> " + validDirections[i]);
        }
    }

    void DebugValidSpaces()
    {
        print("valid spaces for " + (playerTurn ? "black:" : "white:"));

        foreach (var item in validSpaces)
        {
            print(item);
        }
    }

    void PauseEditor()
    {
        UnityEditor.EditorApplication.isPaused = true;
    }

#endif
    #endregion
}