using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MatrixMultiplier
{
    class Program
    {
        #region Variables Declaration

        // Used to track all matrices printed to console.
        static int printedMatrixCounter;

        static Stopwatch stopWatch = new Stopwatch();

        // Used to validate user's input is a number.
        static bool isInt;
        static Random rand = new Random();
        static int matricesQuantity;
        static int matrixSize;
        // Keeps all created matrices until a thread will multiply them, so they can be removed from the list.
        static List<uint[,]> matrixList = new List<uint[,]>();

        // Keeps track of all pending tasks to be done by the threadpool (Creating, multiplying and printing matrices).
        static Queue<Action> taskQueue = new Queue<Action>();
        // Changed to true when an App session is done (after final result matrix is calculated and printed).
        static bool isWorkDone = true;

        static int threadsCount;
        static List<Thread> threadPool = new List<Thread>();
        // Used to keep track of all threads' status (where true means a thread is processing some task off the taskQueue).
        static Dictionary<int, bool> threadStatusList = new Dictionary<int, bool>();
        // Used to change thread's state into sleep mode when it finished to process a task and taskQueue is empty, so it won't eat the CPU resources. << Doc here
        static ManualResetEvent manualResetEvent = new ManualResetEvent(false);

        // Each locker is used for a different scenario where a "race condition" might happen - names should be self explanatory
        // (e.g: _printLocker used to ensure only thread is printing to console at a time).
        static readonly object _printLocker = new object();
        static readonly object _queueLocker = new object();
        static readonly object _threadStatusLocker = new object();
        static readonly object _matrixListlocker = new object();

        #endregion

        static void Main(string[] args)
        {
            do
            {
                Console.WriteLine("Please choose number of threads (between 2 to 20):");
                isInt = int.TryParse(Console.ReadLine(), out threadsCount);
            } while (threadsCount < 2 || threadsCount > 20 || !isInt);

            // Creating the thread pool --> check for more documentation inside the method.
            CreateThreadPool(threadsCount);

            // Starting all threads in thread pool, though they would just start and go to sleep using manualResetEvent.WaitOne()
            // so they won't spend CPU resources for no reason as they will spin with no work queued yet.
            foreach (Thread thread in threadPool)
            {
                thread.Start();
            }

            // Refactored rest of the program into a method so it is easier to loop over using recursion (RunApp is being called at the end of it).
            RunApp();
        }

        #region Functions

        static void RunApp()
        {
            do
            {
                Console.WriteLine("Please choose quantity of square matrices (minimum 2):");
                isInt = int.TryParse(Console.ReadLine(), out matricesQuantity);
            } while (matricesQuantity < 2 || !isInt);

            do
            {
                Console.WriteLine("Please choose the dimension of the square matrices:");
                isInt = int.TryParse(Console.ReadLine(), out matrixSize);
            } while (matrixSize < 1 || !isInt);

            stopWatch.Start();

            // isWorkDone <<< Continue here!
            isWorkDone = false;
            for (int i = 0; i < matricesQuantity; i++)
            {
                string matrixName = "matrix " + (i + 1).ToString();
                // Explain why I used Queue<Action> and used lambda so I can store all type of methods (Actions/Func etc)
                // Additionaly I need to invoke method by using Dequeue()() 
                // so I can get the actual method into a variable and then run the method at a later time
                taskQueue.Enqueue(() => { CreateMatrix(matrixSize, matrixName); });

                if (i == 0)
                {
                    Thread.Sleep(100);
                    // Explain why I awake threads only in the first time (so they can start and check queue status whilte tasks being added)
                    manualResetEvent.Set();
                }
            }
            // Explain how Reset signals thread that if they get an option to sleep they can do so.
            manualResetEvent.Reset();

            UpdateTaskQueue();

            isWorkDone = true;

            stopWatch.Stop();
            GetStatistics();
            ClearResources();

            RunApp();
        }

        private static void ClearResources()
        {
            matrixList.Clear();
            taskQueue.Clear();
            threadStatusList.Clear();
            printedMatrixCounter = 0;
            stopWatch.Reset();
        }

        private static void GetStatistics()
        {
            if (!IsAnyThreadWorking())
            {
                lock (_printLocker) //test start
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Calculating App Stats...");
                    Console.ResetColor();
                }
                Thread.Sleep(100); //test end
                lock (_printLocker)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("================================================================================================================");
                    Console.WriteLine($"Total Matrices: {matricesQuantity}\nMatrix Dimension: {matrixSize}\nTotal Thread: {threadPool.Count}\nTime Elapsed: {stopWatch.Elapsed}");
                    Console.WriteLine($"Printed Matrix Counter: {printedMatrixCounter}, Should be: {(matricesQuantity * 2) - 1}"); //test
                    Console.WriteLine("================================================================================================================\n");
                    Console.ResetColor();
                }
            }
        }

        private static void CreateThreadPool(int threadPoolSize)
        {
            for (int i = 0; i < threadPoolSize; i++)
            {
                Thread thread = CreateThread(i);

                threadPool.Add(thread);
                threadStatusList.Add(int.Parse(thread.Name), false);
            }
        }

        static Thread CreateThread(int index)
        {
            return new Thread(() =>
            {
                Action action;
                while (true)
                {
                    while (!isWorkDone)
                    {
                        action = null;
                        lock (_queueLocker)
                        {
                            if (taskQueue.Count > 0)
                            {
                                action = taskQueue.Dequeue();
                                lock (_threadStatusLocker)
                                {
                                    threadStatusList[index] = true;
                                }
                            }
                        }
                        action?.Invoke();

                        lock (_threadStatusLocker)
                        {
                            threadStatusList[index] = false;
                        }
                    }
                    if (taskQueue.Count == 0)
                    {
                        lock (_printLocker)
                        {
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine($"Thread is going into sleep mode... (Name: {Thread.CurrentThread.Name}, ID: {Thread.CurrentThread.ManagedThreadId})");
                            Console.ResetColor();
                        }
                        manualResetEvent.WaitOne();
                        Thread.Sleep(100);
                    }
                }
            })
            { Name = (index + 1).ToString() };
        }

        static void CreateMatrix(int matrixSize, string matrixName)
        {
            uint[,] matrix = new uint[matrixSize, matrixSize];
            for (int i = 0; i < matrixSize; i++)
            {
                for (int j = 0; j < matrixSize; j++)
                {
                    matrix[i, j] = (uint)rand.Next(0, 10);
                }
            }
            lock (_matrixListlocker)
            {
                matrixList.Add(matrix);
                printedMatrixCounter++; //test
            }
            lock (_printLocker)
            {
                PrintMatrix(matrix, matrixName);
            }
        }

        static void MultiplyMatrices(uint[,] matrixA, uint[,] matrixB)
        {
            uint[,] resultMatrix = new uint[matrixSize, matrixSize];
            for (int i = 0; i < matrixSize; i++)
            {
                for (int j = 0; j < matrixSize; j++)
                {
                    uint[] rowInMatrixA = new uint[matrixSize];
                    uint[] columnInMatrixB = new uint[matrixSize];
                    for (int k = 0; k < matrixSize; k++)
                    {
                        rowInMatrixA[k] = matrixA[i, k];
                        columnInMatrixB[k] = matrixB[k, j];
                        resultMatrix[i, j] += rowInMatrixA[k] * columnInMatrixB[k];
                        // TODO: Handle figures exceeding uint limit. For now those big numbers result in a negative result (Integer Overflow).
                    }
                }
            }
            string matrixName = "Result Matrix";
            if (matrixList.Count == 0 && taskQueue.Count == 0)
            {
                lock (_queueLocker)
                {
                    lock (_threadStatusLocker)
                    {
                        // Check how to change locker.
                        matrixName = matrixList.Count == 0 && taskQueue.Count == 0
                            && threadStatusList.Where(threadStatus => threadStatus.Value == true).Count() == 1
                            ? "FINAL Result Matrix" : "Result Matrix";
                        // Change to ensure this is the final matrix (add condition to check this is the last thread working)
                    }
                }
            }
            lock (_matrixListlocker)
            {
                matrixList.Insert(0, resultMatrix);
                printedMatrixCounter++; //test
            }
            lock (_printLocker)
            {
                PrintMatrix(resultMatrix, matrixName);
            }
        }

        static void PrintMatrix(uint[,] matrix, string matrixName)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"-------------------------------- Thread Name: {Thread.CurrentThread.Name}; Thread ID: {Thread.CurrentThread.ManagedThreadId} --------------------------------\n");
            //TODO: Fix formatting.

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"---------------------------------- {matrixName} ----------------------------------\n");
            Console.ResetColor();

            for (int i = 0; i < matrixSize; i++)
            {
                for (int j = 0; j < matrixSize; j++)
                {
                    Console.Write($"{matrix[i, j],-3} ");
                    //TODO: Fix formatting.
                }
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        static void UpdateTaskQueue()
        {
            manualResetEvent.Set();

            uint[,] matrixA;
            uint[,] matrixB;

            while (isQueueFullOrPending())
            {
                lock (_matrixListlocker)
                {
                    while (matrixList.Count >= 2)
                    {
                        matrixA = matrixList[matrixList.Count - 2];
                        matrixB = matrixList[matrixList.Count - 1];
                        matrixList.RemoveRange(matrixList.Count - 2, 2);
                        lock (_queueLocker)
                        {
                            taskQueue.Enqueue(() => { MultiplyMatrices(matrixA, matrixB); });
                            //isWorkDone = false;
                            manualResetEvent.Set();
                        }
                    }
                }
            }
            manualResetEvent.Reset();
            Thread.Sleep(100); // Check if there is better implementation.
            if (isQueueFullOrPending())
            {
                UpdateTaskQueue();
            }
        }

        static bool isQueueFullOrPending()
        {
            lock (_matrixListlocker)
            {
                lock (_queueLocker)
                {
                    lock (_threadStatusLocker)
                    {
                        if (matrixList.Count >= 2 || taskQueue.Count > 0 || IsAnyThreadWorking())
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }
        }

        static bool IsAnyThreadWorking()
        {
            lock (_threadStatusLocker)
            {
                return threadStatusList.Any(threadStatus => threadStatus.Value == true);
            }
        }

        #endregion
    }
}
