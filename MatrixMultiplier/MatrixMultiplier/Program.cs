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

        // Used to track all matrices printed to console and validate all matrices were printed. 
        // The actual number of matrices to be printed where [n = matices count] is [( n * 2 ) - 1].
        static int printedMatrixCounter;

        static Stopwatch stopWatch = new Stopwatch();

        // Used to validate that user's input is a number.
        static bool isInt;
        static Random rand = new Random();
        static int matricesQuantity;
        static int matrixSize;
        // Keeps all created matrices until a thread will multiply them -> and then they will be removed from the list.
        static List<uint[,]> matrixList = new List<uint[,]>();

        // Keeps track of all pending tasks to be done by the threadpool (which includes: Creating, multiplying and printing matrices).
        static Queue<Action> taskQueue = new Queue<Action>();
        // Changed to true when an App session is done (after final result matrix is calculated and printed),
        // Changed to false at the beginning of each App session (before printing matrices tasks being loaded into the queue).
        static bool isWorkDone = true;

        static int threadsCount;
        static List<Thread> threadPool = new List<Thread>();
        // Used to keep track of all threads' status (where true indicating a thread is processing some task from taskQueue).
        static Dictionary<int, bool> threadStatusList = new Dictionary<int, bool>();
        // Used to change thread's state into sleep mode when it finished to process a task and taskQueue is empty, for 2 reasons:
        // 1) Thread won't eat the CPU resources due to running while there is no work to be done.
        // 2) Thread won't slow other threads' work when it runs in a loop and obtains locks just to check if there are new tasks in queue -->
        // The main thread will be in charge to singal worker threads when they need to wake from their sleep and check queue for new pending tasks.
        static ManualResetEvent manualResetEvent = new ManualResetEvent(false);

        // Each locker is used for a different scenario where a "race condition" might happen in a "critical section"
        // Names should be self explanatory (e.g: _printLocker used to ensure only one thread is printing to console at a time).
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

            // Creating the thread pool --> check for more documentation inside this method.
            CreateThreadPool(threadsCount);

            // Starting all threads in thread pool, though they would just start and go to sleep because of manualResetEvent.WaitOne() being called
            // so they won't spend CPU resources and obtain locks for no reason with no work queued yet.
            foreach (Thread thread in threadPool)
            {
                thread.Start();
            }

            // Refactored rest of the program into a method so it is easier to loop over using recursion (RunApp is being called at the end of itself).
            RunApp();
        }

        #region Functions

        private static void CreateThreadPool(int threadPoolSize)
        {
            for (int i = 0; i < threadPoolSize; i++)
            {
                Thread thread = CreateThread(i);

                threadPool.Add(thread);
                // Updating each thread status to false, which means thread is not processing any task currently.
                threadStatusList.Add(int.Parse(thread.Name), false);
            }
        }

        static Thread CreateThread(int index)
        {
            // Creating a new thread with its job (in lambda expression) to be processed when Thread.Start() will be called.
            return new Thread(() =>
            {
                // Action is created before while(true) infinite loop to avoid creating the object on each loop.
                Action action;
                // Running while(true) in order to keep thread alive for the length of the app.
                // Otherwise, the thread will finish its work and die (go to "Stopped" state) which is not desired for threadpool design.
                while (true)
                {
                    // isWorkDone is a boolean flag which is before queue first load and after its last load.
                    while (!isWorkDone)
                    {
                        // Reseting action in case it has value from previous loop run.
                        action = null;
                        // Locking queue so taskQueue won't be altered by other threads.
                        lock (_queueLocker)
                        {
                            if (taskQueue.Count > 0)
                            {
                                // If queue is full -> pull one task and save it in action (the queue is a list of lambda expressions,
                                // where each lambda RETURNS the original function to be processed.
                                // See taskQueue.Enqueue() on RunApp() for more details.
                                action = taskQueue.Dequeue();
                                lock (_threadStatusLocker)
                                {
                                    // Indicating the thread is working on a task from queue.
                                    threadStatusList[index] = true;
                                }
                            }
                        }
                        // Ternary condition which check if action is null (in which case it returns null), otherwise, it will run Invoke.
                        // Invoke will run the action method (which stores a method from queue).
                        action?.Invoke();

                        lock (_threadStatusLocker)
                        {
                            // Indicating the thread is NOT working on a task from queue.
                            threadStatusList[index] = false;
                        }
                    }
                    // When isWorkDone == true, checking for last time if queue is indeed empty before thread will go to sleep.
                    if (taskQueue.Count == 0)
                    {
                        lock (_printLocker)
                        {
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine($"Thread is going into sleep mode... (Name: {Thread.CurrentThread.Name}, ID: {Thread.CurrentThread.ManagedThreadId})");
                            Console.ResetColor();
                        }
                        // Thread go to sleep until main thread will call manualResetEvent.Set() and wake up all threads.
                        manualResetEvent.WaitOne();
                        Thread.Sleep(100);
                    }
                }
            })
            { Name = (index + 1).ToString() }; // Name is a property of Thread and just used to print thread's details to console.
        }

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

            // Signaling threads that they should check queue on next wake up.
            isWorkDone = false;
            for (int i = 0; i < matricesQuantity; i++)
            {
                string matrixName = "matrix " + (i + 1).ToString();
                // I used Queue<Action> and used lambda so I can store method with parameters too.
                // Additionaly I can dequeue the lambda into another Action object and store the method for a later use 
                // (which I in order to let threads Dequeue() inside a lock, 
                // and then run the method stored once outside the lock to so it could be freed.
                taskQueue.Enqueue(() => { CreateMatrix(matrixSize, matrixName); });
                if (i == 0)
                {
                    // I awake threads only in the first loop run so they can start checking queue status while tasks are being added.
                    // manualResetEvent.Set() basically tells all threads to wake up and stay awake even if they callmanualResetEvent.WaitOne()
                    manualResetEvent.Set();
                }
            }
            // manualResetEvent.Reset() signals all threads that the next time they call callmanualResetEvent.WaitOne() they should go to sleep.
            manualResetEvent.Reset();

            // Pushing multiplying tasks into the queue. Read more inside the method.
            UpdateTaskQueue();

            // Signaling threads that they should NO LONGER check queue for new tasks.
            isWorkDone = true;

            stopWatch.Stop();
            PrintStatistics();
            // Clear all relevant resources so next AppRun() will run with fresh data.
            ClearResources();

            // Using recursion to run app indifinitely.
            RunApp();
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
                printedMatrixCounter++;
            }
            lock (_printLocker)
            {
                PrintMatrix(matrix, matrixName);
            }
        }

        static void PrintMatrix(uint[,] matrix, string matrixName)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"-------------------------------- Thread Name: {Thread.CurrentThread.Name}; Thread ID: {Thread.CurrentThread.ManagedThreadId} --------------------------------\n");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"---------------------------------- {matrixName} ----------------------------------\n");
            Console.ResetColor();

            for (int i = 0; i < matrixSize; i++)
            {
                for (int j = 0; j < matrixSize; j++)
                {
                    Console.Write($"{matrix[i, j],-3} ");
                }
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        static void UpdateTaskQueue()
        {
            // Waking up all threads.
            manualResetEvent.Set();

            // I used uint in order to get bigger maximum positive number (uint range is same int only it stores only positive numbers).
            // The reason for that is because when numbers in the matrices exceed int's limit, the printed results will turn into negative numbers.
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
                        // I made sure no deadlocks are possible becuase of nested of locking.
                        lock (_queueLocker)
                        {
                            taskQueue.Enqueue(() => { MultiplyMatrices(matrixA, matrixB); });
                        }
                    }
                }
            }
            // Signal all threads they can go to sleep the next time they call WaitOne().
            manualResetEvent.Reset();
            Thread.Sleep(100);
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
                    }
                }
            }

            string matrixName = "Result Matrix";
            // ((matricesQuantity * 2) - 1 - 1) is the final matrix to be printed.
            if (printedMatrixCounter == ((matricesQuantity * 2) - 1 - 1))
            {
                matrixName = "FINAL Result Matrix";
                Thread.Sleep(100);
            }

            lock (_matrixListlocker)
            {
                matrixList.Insert(0, resultMatrix);
                printedMatrixCounter++;
            }
            lock (_printLocker)
            {
                PrintMatrix(resultMatrix, matrixName);
            }
        }

        private static void PrintStatistics()
        {
            if (!IsAnyThreadWorking())
            {
                lock (_printLocker)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Calculating App Stats...");
                    Console.ResetColor();
                }
                Thread.Sleep(100);
                lock (_printLocker)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("================================================================================================================");
                    Console.WriteLine($"Total Matrices: {matricesQuantity}\nMatrix Dimension: {matrixSize}\nTotal Thread: {threadPool.Count}\nTime Elapsed: {stopWatch.Elapsed}");
                    Console.WriteLine($"Printed Matrices Counter: {printedMatrixCounter}, Should be: {(matricesQuantity * 2) - 1}");
                    Console.WriteLine("================================================================================================================\n");
                    Console.ResetColor();
                }
            }
        }

        private static void ClearResources()
        {
            matrixList.Clear();
            taskQueue.Clear();
            threadStatusList.Clear();

            printedMatrixCounter = 0;
            stopWatch.Reset();
        }

        #endregion
    }
}
